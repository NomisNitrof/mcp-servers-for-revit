using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Modifie les paramètres d'un type existant (FamilySymbol ou type système)
    /// dans le document de projet. Séparé de duplicate_family_type pour éviter
    /// les duplications accidentelles. Une seule transaction ; les paramètres
    /// introuvables ou refusés finissent dans skipped, jamais d'échec silencieux.
    /// </summary>
    public class SetFamilyTypeParametersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée
        public long TypeId { get; set; }
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

                using (var transaction = new Transaction(doc, "MCP — Modifier les paramètres d'un type"))
                {
                    transaction.Start();
                    FamilyParameterUtils.ApplySpecs(elementType, Parameters, applied, skipped);
                    transaction.Commit();
                }

                Result = new
                {
                    typeId = TypeId,
                    typeName = elementType.Name,
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
            return "SetFamilyTypeParameters";
        }
    }
}
