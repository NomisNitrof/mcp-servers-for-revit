using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Supprime un type (FamilySymbol ou type système) du document de projet.
    /// Supprimer un type supprime AUSSI ses occurrences placées : sans
    /// Force, la commande refuse s'il en existe et renvoie leur nombre.
    /// </summary>
    public class DeleteFamilyTypeEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée
        public long TypeId { get; set; }
        public bool Force { get; set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            IsSuccess = false;
            ErrorMessage = null;
            Result = null;
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    ErrorMessage = "aucun document actif dans Revit";
                    return;
                }

                var typeId = FamilyParameterUtils.IdFromValue(TypeId);
                var elementType = doc.GetElement(typeId) as ElementType;
                if (elementType == null)
                {
                    ErrorMessage = $"aucun type (ElementType) avec l'id {TypeId}";
                    return;
                }

                string typeName = elementType.Name;
                string familyName = elementType.FamilyName;

                // Garde-fou : compter les occurrences qui seraient emportées.
                int instances = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.GetTypeId() == typeId)
                    .Count();
                if (instances > 0 && !Force)
                {
                    ErrorMessage = $"le type « {typeName} » a {instances} occurrence(s) placée(s) qui " +
                                   "seraient supprimées avec lui — relancer avec force: true pour confirmer";
                    return;
                }

                int deletedCount;
                using (var transaction = new Transaction(doc, "MCP — Supprimer un type"))
                {
                    transaction.Start();
                    deletedCount = doc.Delete(typeId).Count;
                    transaction.Commit();
                }

                Result = new
                {
                    typeId = TypeId,
                    typeName,
                    familyName,
                    instancesDeleted = instances,
                    deletedCount
                };
                IsSuccess = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "DeleteFamilyType";
        }
    }
}
