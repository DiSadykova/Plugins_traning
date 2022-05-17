using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CreateRoomsPlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CreateRooms : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            List<Level> levels = new FilteredElementCollector(doc)
                                   .OfClass(typeof(Level))
                                   .OfType<Level>()
                                   .ToList();
            if (levels == null)
            {
                TaskDialog.Show("Ошибка", "Уровни не найдены");
                return Result.Cancelled;
            }
            foreach (Level level in levels)
            {
                CreateRoomsAndRoomTag(doc, level);
            }
            return Result.Succeeded;
        }

        private static void CreateRoomsAndRoomTag(Document doc, Level level)
        {
            Transaction transaction = new Transaction(doc, "Cоздание помещений и марок");
            transaction.Start();
            ICollection<ElementId> newRoomsId = doc.Create.NewRooms2(level);

            foreach (ElementId newRoomId in newRoomsId)
            {

                Room room = doc.GetElement(newRoomId) as Room;
                string levelName = room.Level.Name.Substring(8);
                room.Name = $"{levelName}_{room.Number}";

                LocationPoint locationPoint = room.Location as LocationPoint;
                UV roomTagLocation = new UV(locationPoint.Point.X, locationPoint.Point.Y);

                RoomTag roomTag = doc.Create.NewRoomTag(new LinkElementId(newRoomId), roomTagLocation, null);
            }
            transaction.Commit();
        }
    }
}
