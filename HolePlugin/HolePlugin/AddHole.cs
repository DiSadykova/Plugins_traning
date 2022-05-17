using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HolePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class AddHole : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document arDoc = commandData.Application.ActiveUIDocument.Document;

            Document ovDoc = arDoc.Application.Documents.OfType<Document>().Where(x => x.Title.Contains("ОВ")).FirstOrDefault();
            if (ovDoc == null)
            {
                TaskDialog.Show("Ошибка", "Не найден ОВ файл");
                return Result.Cancelled;
            }

            FamilySymbol familySymbol = GetFamilySymbolGenericModel(arDoc);
            if (familySymbol == null)
            {
                TaskDialog.Show("Ошибка", "Не найдено семейство \"Отверстия\"");
                return Result.Cancelled;
            }
            List<Duct> ducts = GetDucts(ovDoc);

            List<Pipe> pipes = GetPipes(ovDoc);

            View3D view3D = GetView3D(arDoc);

            if (view3D == null)
            {
                TaskDialog.Show("Ошибка", "Не найден 3D вид");
                return Result.Cancelled;
            }

            Transaction transaction0 = new Transaction(arDoc);
            transaction0.Start("Расстановка  отверстий");

            if (!familySymbol.IsActive)
            {
                familySymbol.Activate();
            }
            transaction0.Commit();

            Transaction transaction = new Transaction(arDoc);
            transaction.Start("Расстановка отверстий");

            foreach (Duct d in ducts)
            {
                CreateHole(arDoc, familySymbol, d, view3D);
            }
            foreach (Pipe p in pipes)
            {
                CreateHole(arDoc, familySymbol, p, view3D);
            }
            transaction.Commit();
            return Result.Succeeded;
        }

        private static void CreateHole(Document arDoc, FamilySymbol familySymbol,  Element d, View3D view3D)
        {
            ReferenceIntersector referenceIntersector = new ReferenceIntersector(new ElementClassFilter(typeof(Wall)), FindReferenceTarget.Element, view3D);
            Pipe pipe = d as Pipe;
            Duct duct = d as Duct;
            Line curve = (d.Location as LocationCurve).Curve as Line;
            XYZ point = curve.GetEndPoint(0);
            XYZ direction = curve.Direction;

            List<ReferenceWithContext> intersections = referenceIntersector.Find(point, direction)
                .Where(x => x.Proximity <= curve.Length)
                .Distinct(new ReferenceWithContextElementEqualityComparer())
                .ToList();
            foreach (ReferenceWithContext refer in intersections)
            {
                double proximity = refer.Proximity;
                Reference reference = refer.GetReference();
                Wall wall = arDoc.GetElement(reference.ElementId) as Wall;
                Level level = arDoc.GetElement(wall.LevelId) as Level;
                XYZ pointHole = point + (direction * proximity);

                FamilyInstance hole = arDoc.Create.NewFamilyInstance(pointHole, familySymbol, wall, level, StructuralType.NonStructural);
                Parameter width = hole.LookupParameter("Ширина");
                Parameter height = hole.LookupParameter("Выстота");
                if (pipe!= null)
                {
                    width.Set(pipe.Diameter);
                    height.Set(pipe.Diameter);
                }
                else
                {
                    width.Set(duct.Diameter);
                    height.Set(duct.Diameter);
                }
            }
        }

        private static FamilySymbol GetFamilySymbolGenericModel(Document arDoc)
        {
            return new FilteredElementCollector(arDoc)
                            .OfClass(typeof(FamilySymbol))
                            .OfCategory(BuiltInCategory.OST_GenericModel)
                            .OfType<FamilySymbol>()
                            .Where(x => x.FamilyName.Equals("Отверстие"))
                            .FirstOrDefault();
        }

        private static View3D GetView3D(Document arDoc)
        {
            return new FilteredElementCollector(arDoc)
                .OfClass(typeof(View3D))
                .OfType<View3D>()
                .Where(x => !x.IsTemplate)
                .FirstOrDefault();

        }

        private static List<Pipe> GetPipes(Document ovDoc)
        {
            return new FilteredElementCollector(ovDoc)
                .OfClass(typeof(Pipe))
                .OfType<Pipe>()
                .ToList();
        }

        private static List<Duct> GetDucts(Document ovDoc)
        {
            return new FilteredElementCollector(ovDoc)
                .OfClass(typeof(Duct))
                .OfType<Duct>()
                .ToList();
        }
    }

    internal class ReferenceWithContextElementEqualityComparer : IEqualityComparer<ReferenceWithContext>
    {
        public bool Equals(ReferenceWithContext x, ReferenceWithContext y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(null, x)) return false;
            if (ReferenceEquals(null, y)) return false;

            var xReference = x.GetReference();
            var yReference = y.GetReference();

            return xReference.LinkedElementId == yReference.LinkedElementId
                && xReference.ElementId == yReference.ElementId;
        }

        public int GetHashCode(ReferenceWithContext obj)
        {
            var reference=obj.GetReference();

            unchecked
            {
                return (reference.LinkedElementId.GetHashCode() * 397) ^ reference.ElementId.GetHashCode();
            }
        }
    }
}
