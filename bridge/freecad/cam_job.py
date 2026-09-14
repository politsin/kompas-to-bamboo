import json
import os
import sys
import traceback

import FreeCAD as App
import Import
import Part
from Path.Main import Job
from Path.Post.scripts import grbl_legacy_post
from Path.Main.Job import PathToolController
from Path.Op import Drilling
from Path.Op import Pocket
from Path.Op import Profile
from Path.Tool.toolbit import ToolBitBallend, ToolBitDrill, ToolBitEndmill

DRILL_SIZES_MM = [round(size / 10, 1) for size in range(6, 31)]


def write_status(path, **payload):
    with open(path, "w", encoding="utf-8") as status_file:
        json.dump(payload, status_file, ensure_ascii=False, indent=2)


def quantity(value):
    return App.Units.Quantity("{} mm".format(value))


def add_tool(document, job, tool_class, shape_id, label, diameter, number):
    tool_bit = tool_class.from_shape_id(shape_id)
    tool_bit.set_diameter(quantity(diameter))
    tool = tool_bit.attach_to_doc(document)
    tool.Label = label
    controller = PathToolController.Create("TC: " + label, tool, number)
    job.Proxy.addToolController(controller)
    return controller


def selected_drill_size(hole_diameter):
    candidates = [size for size in DRILL_SIZES_MM if size <= hole_diameter + 0.05]
    return candidates[-1] if candidates else None


def model_bounds(objects):
    boxes = [obj.Shape.BoundBox for obj in objects]
    return {
        "xmin": min(box.XMin for box in boxes),
        "xmax": max(box.XMax for box in boxes),
        "ymin": min(box.YMin for box in boxes),
        "ymax": max(box.YMax for box in boxes),
        "zmin": min(box.ZMin for box in boxes),
        "zmax": max(box.ZMax for box in boxes),
    }


def find_vertical_holes(objects, origin):
    holes = []
    for obj in objects:
        for face in obj.Shape.Faces:
            surface = face.Surface
            if not isinstance(surface, Part.Cylinder) or abs(surface.Axis.z) < 0.999:
                continue
            # For a solid, a reversed cylindrical face is an internal wall.
            if face.Orientation != "Reversed":
                continue
            diameter = round(float(surface.Radius) * 2, 4)
            drill_size = selected_drill_size(diameter)
            if drill_size is None:
                continue
            holes.append({
                "diameter": diameter,
                "drill": drill_size,
                # LUNYEE WCS: left-near corner of stock top is X0 Y0 Z0.
                "point": [surface.Center.x - origin["xmin"], surface.Center.y - origin["ymin"],
                          face.BoundBox.ZMax - origin["zmax"]],
                "depth": face.BoundBox.ZMin - face.BoundBox.ZMax,
            })
    return holes


def create_corn_outline(job, model, controller, bounds):
    """Cut the complete outside contour when the STEP has no drillable holes."""
    operation = Profile.Create("Corn mill outside contour", parentJob=job)
    operation.Label = "Corn endmill 3 mm — outside contour"
    # An empty subelement list tells Path to use the complete model outline.
    operation.Base = [(model, [])]
    operation.ToolController = controller
    operation.Side = "Outside"
    operation.processHoles = False
    operation.processCircles = False
    operation.processPerimeter = True
    operation.StepDown = quantity(1.0)
    operation.FinalDepth = quantity(bounds["zmin"] - bounds["zmax"])
    return operation


def create_corn_facing(job, model, controller, bounds):
    """Clear the largest horizontal model face, preserving islands such as pins."""
    horizontal_faces = []
    for index, face in enumerate(model.Shape.Faces, 1):
        box = face.BoundBox
        if box.ZLength > 0.001:
            continue
        try:
            normal = face.normalAt(0, 0)
        except Exception:
            continue
        if normal.z < 0.999:
            continue
        horizontal_faces.append((face.Area, index))
    if not horizontal_faces:
        return None

    _, face_index = max(horizontal_faces)
    top_face = model.Shape.Faces[face_index - 1]
    operation = Pocket.Create("Corn mill top surface", parentJob=job)
    operation.Label = "Corn endmill 3 mm — top surface around pins"
    operation.Base = [(model, ["Face{}".format(face_index)])]
    operation.ToolController = controller
    operation.StepOver = 40
    operation.StepDown = quantity(1.0)
    # The work origin is the highest model point (the pin tips).  Clear down
    # to the broad planar face, retaining the islands bounded by that face.
    operation.StartDepth = quantity(0)
    operation.FinalDepth = quantity(top_face.BoundBox.ZMax - bounds["zmax"])
    return operation


def create_job(step_path, output_path, status_path):
    document = App.newDocument("Kompas_CAM_Job")
    Import.insert(step_path, document.Name)
    imported_shapes = [obj.Shape for obj in document.Objects if hasattr(obj, "Shape") and not obj.Shape.isNull()
                       and obj.Shape.BoundBox.XLength > 0 and obj.Shape.BoundBox.YLength > 0]
    if not imported_shapes:
        raise RuntimeError("STEP did not contain a solid suitable for a CAM job.")

    # STEP assemblies may import as several document objects. Path's stock
    # generator needs one finite CAM base, so preserve their geometry in a
    # single compound rather than passing the importer objects directly.
    cam_model = document.addObject("PartDesign::Feature", "CAM_Model")
    cam_model.Label = "Imported STEP CAM base"
    cam_model.Shape = Part.makeCompound(imported_shapes)
    document.recompute()
    objects = [cam_model]

    bounds = model_bounds(objects)
    dimensions = (bounds["xmax"] - bounds["xmin"], bounds["ymax"] - bounds["ymin"], bounds["zmax"] - bounds["zmin"])
    if min(dimensions) <= 0:
        raise RuntimeError("STEP CAM base has a zero bounding-box dimension: {}".format(dimensions))
    job = Job.Create("CAM_Job", objects)
    job.Label = "KOMPAS CAM Job"
    job.PostProcessor = "grbl"
    job.addProperty("App::PropertyString", "MachineProfile", "KOMPAS CAM")
    job.MachineProfile = "LUNYEE 4040 Titan (GRBL, 400 x 400 x 95 mm, spindle <= 12000 RPM)"
    job.addProperty("App::PropertyString", "WorkCoordinateOrigin", "KOMPAS CAM")
    job.WorkCoordinateOrigin = "Xmin/Ymin/Zmax: left-near top corner of stock"
    job.addProperty("App::PropertyVector", "WorkCoordinateModelPoint", "KOMPAS CAM")
    job.WorkCoordinateModelPoint = App.Vector(bounds["xmin"], bounds["ymin"], bounds["zmax"])

    controllers = {}
    tool_number = 10
    for size in DRILL_SIZES_MM:
        controllers[("drill", size)] = add_tool(
            document, job, ToolBitDrill, "drill", "Drill {:.1f} mm".format(size), size, tool_number)
        tool_number += 1
    controllers[("corn", 1.5)] = add_tool(document, job, ToolBitEndmill, "endmill", "Corn endmill 1.5 mm", 1.5, tool_number)
    tool_number += 1
    controllers[("corn", 3.0)] = add_tool(document, job, ToolBitEndmill, "endmill", "Corn endmill 3 mm", 3.0, tool_number)
    tool_number += 1
    controllers[("ball", 3.0)] = add_tool(document, job, ToolBitBallend, "ballend", "Ball-nose endmill 3 mm", 3.0, tool_number)

    holes = find_vertical_holes(objects, bounds)
    holes_by_drill = {}
    for hole in holes:
        holes_by_drill.setdefault(hole["drill"], []).append(hole)
    for size, matched_holes in holes_by_drill.items():
        operation = Drilling.Create("Drill {:.1f} mm".format(size), parentJob=job)
        operation.ToolController = controllers[("drill", size)]
        operation.Locations = [App.Vector(*hole["point"]) for hole in matched_holes]
        operation.FinalDepth = min(hole["depth"] for hole in matched_holes)
        operation.PeckEnabled = True
        operation.PeckDepth = quantity(0.5)

    fallback_operation = None
    if not holes:
        create_corn_facing(job, cam_model, controllers[("corn", 3.0)], bounds)
        fallback_operation = create_corn_outline(
            job, cam_model, controllers[("corn", 3.0)], bounds)

    document.recompute()
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    gcode_path = os.path.splitext(output_path)[0] + ".gcode"
    # The Job property is an App::DocumentObjectGroup; the postprocessor
    # expects its concrete operation objects, not the group container.
    grbl_legacy_post.export(job.Operations.Group, gcode_path, "")
    if not os.path.exists(gcode_path) or os.path.getsize(gcode_path) == 0:
        raise RuntimeError("GRBL postprocessor did not create G-code.")
    document.saveAs(output_path)
    write_status(status_path, state="created", outputFile=output_path, holes=holes,
                 machine="LUNYEE 4040 Titan", postProcessor="grbl", workCoordinateOrigin="Xmin/Ymin/Zmax",
                 gcodeFile=gcode_path, drillOperations=len(holes_by_drill),
                 fallbackOperation=(fallback_operation.Label if fallback_operation else None),
                 toolControllers=len(job.Tools.Group))


def main():
    input_path = os.environ["KOMPAS_BAMBU_CAM_INPUT"]
    output_path = os.environ["KOMPAS_BAMBU_CAM_OUTPUT"]
    status_path = os.environ["KOMPAS_BAMBU_CAM_STATUS"]
    try:
        create_job(os.path.abspath(input_path), os.path.abspath(output_path), os.path.abspath(status_path))
    except Exception as error:
        write_status(status_path, state="failed", error=str(error), traceback=traceback.format_exc())
        raise


main()
