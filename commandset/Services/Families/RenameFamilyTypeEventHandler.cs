using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Renomme un type (FamilySymbol ou type système) du document de projet.
    /// Une transaction ; le doublon de nom remonte comme erreur nette.
    /// </summary>
    public class RenameFamilyTypeEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée
        public long TypeId { get; set; }
        public string NewName { get; set; }

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

                string oldName = elementType.Name;
                using (var transaction = new Transaction(doc, "MCP — Renommer un type"))
                {
                    transaction.Start();
                    elementType.Name = NewName;
                    transaction.Commit();
                }

                Result = new
                {
                    typeId = TypeId,
                    familyName = elementType.FamilyName,
                    oldName,
                    newName = NewName
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
            return "RenameFamilyType";
        }
    }
}
