using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Services.Families;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Families
{
    /// <summary>
    /// set_element_parameters — modifie les paramètres d'occurrence d'un
    /// élément placé (le pendant écriture de list_element_parameters).
    /// </summary>
    public class SetElementParametersCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SetElementParametersEventHandler _handler => (SetElementParametersEventHandler)Handler;

        public override string CommandName => "set_element_parameters";

        public SetElementParametersCommand(UIApplication uiApp)
            : base(new SetElementParametersEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    long? elementId = parameters?["elementId"]?.Value<long>();
                    if (!elementId.HasValue)
                        throw new ArgumentException("elementId est obligatoire");

                    var specs = parameters?["parameters"]?.ToObject<List<ParameterValueSpec>>();
                    if (specs == null || specs.Count == 0)
                        throw new ArgumentException("parameters est obligatoire et non vide");

                    _handler.ElementId = elementId.Value;
                    _handler.Parameters = specs;

                    if (!RaiseAndWaitForCompletion(20000))
                        throw new TimeoutException("set_element_parameters : délai dépassé (20 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"set_element_parameters a échoué : {ex.Message}");
                }
            }
        }
    }
}
