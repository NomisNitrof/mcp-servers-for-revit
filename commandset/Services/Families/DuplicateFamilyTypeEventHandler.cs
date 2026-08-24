using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Duplique un type (ElementType.Duplicate — couvre FamilySymbol ET types
    /// système comme WallType) dans le document de projet, puis applique les
    /// paramètres fournis au nouveau type. Une seule transaction.
    /// </summary>
    public class DuplicateFamilyTypeEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée
        public long TypeId { get; set; }
        public string NewName { get; set; }
        public List<ParameterValueSpec> Parameters { get; set; }

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

                var elementType = doc.GetElement(FamilyParameterUtils.IdFromValue(TypeId)) as ElementType;
                if (elementType == null)
                {
                    ErrorMessage = $"aucun type (ElementType) avec l'id {TypeId}";
                    return;
                }

                var applied = new List<AppliedParameterInfo>();
                var skipped = new List<SkippedParameterInfo>();
                ElementType newType;

                using (var transaction = new Transaction(doc, "MCP — Dupliquer un type"))
                {
                    transaction.Start();
                    newType = elementType.Duplicate(NewName);
                    FamilyParameterUtils.ApplySpecs(newType, Parameters, applied, skipped);
                    transaction.Commit();
                }

                Result = new
                {
                    newTypeId = newType.Id.GetValue(),
                    newTypeName = newType.Name,
                    sourceTypeId = TypeId,
                    applied,
                    skipped
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
            return "DuplicateFamilyType";
        }
    }
}
