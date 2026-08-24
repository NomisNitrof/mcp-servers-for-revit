using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Services.Families;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Families
{
    /// <summary>
    /// set_family_type_parameters — modifie les paramètres d'un type existant
    /// (FamilySymbol ou type système) du document de projet. Les paramètres
    /// introuvables ou refusés sont renvoyés dans skipped.
    /// </summary>
    public class SetFamilyTypeParametersCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SetFamilyTypeParametersEventHandler _handler => (SetFamilyTypeParametersEventHandler)Handler;

        public override string CommandName => "set_family_type_parameters";

        public SetFamilyTypeParametersCommand(UIApplication uiApp)
            : base(new SetFamilyTypeParametersEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    long? typeId = parameters?["typeId"]?.Value<long>();
                    if (!typeId.HasValue)
                        throw new ArgumentException("typeId est obligatoire");

                    var specs = parameters?["parameters"]?.ToObject<List<ParameterValueSpec>>();
                    if (specs == null || specs.Count == 0)
                        throw new ArgumentException("parameters est obligatoire et non vide");

                    _handler.TypeId = typeId.Value;
                    _handler.Parameters = specs;

                    if (!RaiseAndWaitForCompletion(20000))
                        throw new TimeoutException("set_family_type_parameters : délai dépassé (20 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"set_family_type_parameters a échoué : {ex.Message}");
                }
            }
        }
    }
}
