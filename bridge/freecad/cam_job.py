import json
import os
import sys
import traceback

import FreeCAD as App
import Import
import Part
from Path.Main import Job
from Path.Main.Job import PathToolController
from Path.Op import Drilling
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


def find_vertical_holes(objects):
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
                "point": [surface.Center.x, surface.Center.y, face.BoundBox.ZMax],
                "depth": face.BoundBox.ZMin - face.BoundBox.ZMax,
            })
    return holes


def create_job(step_path, output_path, status_path):
    document = App.newDocument("Kompas_CAM_Job")
    Import.insert(step_path, document.Name)
    objects = [obj for obj in document.Objects if hasattr(obj, "Shape") and not obj.Shape.isNull()]
    if not objects:
        raise RuntimeError("STEP did not contain a solid suitable for a CAM job.")

    job = Job.Create("CAM_Job", objects)
    job.Label = "KOMPAS CAM Job"

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

    holes = find_vertical_holes(objects)
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

    document.recompute()
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    document.saveAs(output_path)
    write_status(status_path, state="created", outputFile=output_path, holes=holes,
                 drillOperations=len(holes_by_drill), toolControllers=len(job.Tools.Group))


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
