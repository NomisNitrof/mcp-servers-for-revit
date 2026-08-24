using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Services.Families;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Families
{
    /// <summary>
    /// duplicate_family_type — duplique un type (ElementType.Duplicate, couvre
    /// FamilySymbol et types système) puis applique les paramètres fournis au
    /// nouveau type, dans une seule transaction du document de projet.
    /// </summary>
    public class DuplicateFamilyTypeCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private DuplicateFamilyTypeEventHandler _handler => (DuplicateFamilyTypeEventHandler)Handler;

        public override string CommandName => "duplicate_family_type";

        public DuplicateFamilyTypeCommand(UIApplication uiApp)
            : base(new DuplicateFamilyTypeEventHandler(), uiApp)
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

                    string newName = parameters?["newName"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(newName))
                        throw new ArgumentException("newName est obligatoire et non vide");

                    _handler.TypeId = typeId.Value;
                    _handler.NewName = newName;
                    _handler.Parameters = parameters?["parameters"]?.ToObject<List<ParameterValueSpec>>();

                    if (!RaiseAndWaitForCompletion(20000))
                        throw new TimeoutException("duplicate_family_type : délai dépassé (20 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"duplicate_family_type a échoué : {ex.Message}");
                }
            }
        }
    }
}
