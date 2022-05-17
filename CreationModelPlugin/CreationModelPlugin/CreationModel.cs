using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.ApplicationServices;

namespace CreationModelPlugin
{
    [TransactionAttribute(TransactionMode.Manual)]
    public class CreationModel : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            Level level1, level2;
            GetLevels(doc, out level1, out level2);

            List<Wall> walls = CreateWalls(doc, level1, level2);

            AddDoors(doc, level1, walls[0]);
            List<FamilyInstance> windows = AddWindows(doc, level1, walls);
            AddRoof(doc, level2, walls);

            return Result.Succeeded;
        }

        private void AddRoof(Document doc, Level level2, List<Wall> walls)
        {
            RoofType roofType = new FilteredElementCollector(doc)
                .OfClass(typeof(RoofType))
                .OfType<RoofType>()
                .Where(n => n.Name.Equals("Типовой - 125мм"))
                .Where(n => n.FamilyName.Equals("Базовая крыша"))
                .FirstOrDefault();

            //вычисление координат основы стены для построения рабочей плоскости и профиля крыши
            double wallWidth = walls[0].Width;
            double dt = wallWidth / 2;
            List<XYZ> points = new List<XYZ>();
            points.Add(new XYZ(-dt, -dt, 0));
            points.Add(new XYZ(dt, -dt, 0));
            points.Add(new XYZ(dt, dt, 0));
            //points.Add(new XYZ(-dt, dt, 0));
            //points.Add(new XYZ(-dt, -dt, 0));

            Application application = doc.Application;
            CurveArray footprint = application.Create.NewCurveArray();
           
            LocationCurve curve = walls[1].Location as LocationCurve;
            XYZ p1 = curve.Curve.GetEndPoint(0);
            XYZ p2 = curve.Curve.GetEndPoint(1);

            //вычисление смещений для точек профиля стены относительно высоты стен 
            XYZ offsetTopPoint= new XYZ(0, 0, UnitUtils.ConvertToInternalUnits(2000, UnitTypeId.Millimeters));
            XYZ offsetWallHeight = new XYZ(0, 0, level2.get_Parameter(BuiltInParameter.LEVEL_ELEV).AsDouble());

           
            //построение точек профиля крыши
            XYZ point1 = p1 + points[1]+ offsetWallHeight;
            XYZ point2= ((p2 + points[2]) + (p1 + points[1])) / 2 + offsetTopPoint+ offsetWallHeight;
            XYZ point3 = p2 + points[2]+ offsetWallHeight;
            
            //вычисление смещения профиля с учетом переменной толщины крыши
            double b = Math.Abs(point2.Y - point1.Y);
            double a = offsetTopPoint.Z;
            double BC = Math.Acos(b / Math.Sqrt(Math.Pow(a, 2) + Math.Pow(b, 2)))*180/Math.PI;
            double x = roofType.get_Parameter(BuiltInParameter.ROOF_ATTR_DEFAULT_THICKNESS_PARAM).AsDouble()/(Math.Sin((90-BC)*Math.PI/180));
            XYZ offsetRoof = new XYZ(0, 0, x);

            point1 += offsetRoof;
            point2 += offsetRoof;
            point3 += offsetRoof;

            Line line1 = Line.CreateBound(point1, point2);
            footprint.Append(line1);
            Line line2 = Line.CreateBound(point2, point3);
            footprint.Append(line2);


            Transaction transaction = new Transaction(doc, "Построение крыши");
            transaction.Start();
            XYZ bubbleEnd = point1 + offsetTopPoint;
            XYZ freeEnd = point1;
            XYZ ThirdPt = point3;
            ReferencePlane plane = doc.Create.NewReferencePlane2(bubbleEnd, freeEnd, ThirdPt, doc.ActiveView);

            doc.Create.NewExtrusionRoof(footprint, plane, level2, roofType, 0, -(walls[0].get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble()+wallWidth));
            transaction.Commit();
            


        }

        private static List<FamilyInstance> AddWindows(Document doc, Level level1, List<Wall> walls)
        {
            FamilySymbol windowType = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Windows)
                .OfType<FamilySymbol>()
                .Where(x => x.Name.Equals("0915 x 1830 мм"))
                .Where(x => x.FamilyName.Equals("Фиксированные"))
                .FirstOrDefault();

            List<FamilyInstance> windows = new List<FamilyInstance>();

            Transaction transaction = new Transaction(doc, "Построение окон");
            transaction.Start();
            for (int i = 1; i < walls.Count; i++)
            {
                Wall wall = walls[i];
                LocationCurve hostCurve = wall.Location as LocationCurve;
                XYZ point1 = hostCurve.Curve.GetEndPoint(0);
                XYZ point2 = hostCurve.Curve.GetEndPoint(1);
                XYZ point = (point1 + point2) / 2;

                if (!windowType.IsActive)
                    windowType.Activate();
                FamilyInstance window = doc.Create.NewFamilyInstance(point, windowType, wall, level1, StructuralType.NonStructural);
                windows.Add(window);

                window.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM).Set(UnitUtils.ConvertToInternalUnits(915, UnitTypeId.Millimeters));
            }
            transaction.Commit();

            return windows;
        }

        private static List<Wall> CreateWalls(Document doc, Level level1, Level level2, double widthSet = 10000, double depthSet = 5000)
        {
            double width = UnitUtils.ConvertToInternalUnits(widthSet, UnitTypeId.Millimeters);
            double depth = UnitUtils.ConvertToInternalUnits(depthSet, UnitTypeId.Millimeters);
            double dx = width / 2;
            double dy = depth / 2;

            List<XYZ> points = new List<XYZ>();
            points.Add(new XYZ(-dx, -dy, 0));
            points.Add(new XYZ(dx, -dy, 0));
            points.Add(new XYZ(dx, dy, 0));
            points.Add(new XYZ(-dx, +dy, 0));
            points.Add(new XYZ(-dx, -dy, 0));

            List<Wall> walls = new List<Wall>();

            Transaction transaction = new Transaction(doc, "Построение стен");
            transaction.Start();
            for (int i = 0; i < 4; i++)
            {
                Line line = Line.CreateBound(points[i], points[i + 1]);
                Wall wall = Wall.Create(doc, line, level1.Id, false);
                walls.Add(wall);
                wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE).Set(level2.Id);
            }
            transaction.Commit();
            return walls;
        }

        private static void AddDoors(Document doc, Level level1, Wall wall)
        {
            FamilySymbol doorType = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Doors)
                .OfType<FamilySymbol>()
                .Where(x => x.Name.Equals("0915 x 2134 мм"))
                .Where(x => x.FamilyName.Equals("Одиночные-Щитовые"))
                .FirstOrDefault();

            LocationCurve hostCurve = wall.Location as LocationCurve;
            XYZ point1 = hostCurve.Curve.GetEndPoint(0);
            XYZ point2 = hostCurve.Curve.GetEndPoint(1);
            XYZ point = (point1 + point2) / 2;

            Transaction transaction = new Transaction(doc, "Построение дверей");
            transaction.Start();
            if (!doorType.IsActive)
                doorType.Activate();
            doc.Create.NewFamilyInstance(point, doorType, wall, level1, StructuralType.NonStructural);
            transaction.Commit();
        }

        private static void GetLevels(Document doc, out Level level1, out Level level2)
        {
            List<Level> listLevel = new FilteredElementCollector(doc)
                            .OfClass(typeof(Level))
                            .OfType<Level>()
                            .ToList();

            level1 = listLevel
                .Where(x => x.Name.Equals("Уровень 1"))
                .FirstOrDefault();

            level2 = listLevel
                .Where(x => x.Name.Equals("Уровень 2"))
                .FirstOrDefault();
        }
    }
}
