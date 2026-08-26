using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Families;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Families
{
    /// <summary>
    /// rename_family_type — renomme un type (FamilySymbol ou type système).
    /// </summary>
    public class RenameFamilyTypeCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private RenameFamilyTypeEventHandler _handler => (RenameFamilyTypeEventHandler)Handler;

        public override string CommandName => "rename_family_type";

        public RenameFamilyTypeCommand(UIApplication uiApp)
            : base(new RenameFamilyTypeEventHandler(), uiApp)
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

                    if (!RaiseAndWaitForCompletion(15000))
                        throw new TimeoutException("rename_family_type : délai dépassé (15 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"rename_family_type a échoué : {ex.Message}");
                }
            }
        }
    }
}
