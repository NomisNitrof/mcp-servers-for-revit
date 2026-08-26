using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Modifie les paramètres d'OCCURRENCE d'un élément placé — le pendant
    /// écriture de ListElementParameters. Une seule transaction ; les
    /// paramètres introuvables ou refusés finissent dans skipped, jamais
    /// d'échec silencieux.
    /// </summary>
    public class SetElementParametersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée
        public long ElementId { get; set; }
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

                var element = doc.GetElement(FamilyParameterUtils.IdFromValue(ElementId));
                if (element == null)
                {
                    ErrorMessage = $"aucun élément avec l'id {ElementId}";
                    return;
                }
                if (element is ElementType)
                {
                    ErrorMessage = $"l'id {ElementId} est un TYPE — utiliser set_family_type_parameters " +
                                   "pour les paramètres de type";
                    return;
                }

                var applied = new List<AppliedParameterInfo>();
                var skipped = new List<SkippedParameterInfo>();

                using (var transaction = new Transaction(doc, "MCP — Modifier les paramètres d'un élément"))
                {
                    transaction.Start();
                    FamilyParameterUtils.ApplySpecs(element, Parameters, applied, skipped);
                    transaction.Commit();
                }

                Result = new
                {
                    elementId = ElementId,
                    name = element.Name,
                    category = element.Category?.Name,
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
            return "SetElementParameters";
        }
    }
}
