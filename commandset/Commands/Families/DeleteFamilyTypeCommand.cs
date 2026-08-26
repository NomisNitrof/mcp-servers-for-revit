using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Families;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Families
{
    /// <summary>
    /// delete_family_type — supprime un type ; refuse sans force: true si des
    /// occurrences placées seraient emportées avec lui.
    /// </summary>
    public class DeleteFamilyTypeCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private DeleteFamilyTypeEventHandler _handler => (DeleteFamilyTypeEventHandler)Handler;

        public override string CommandName => "delete_family_type";

        public DeleteFamilyTypeCommand(UIApplication uiApp)
            : base(new DeleteFamilyTypeEventHandler(), uiApp)
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

                    _handler.TypeId = typeId.Value;
                    _handler.Force = parameters?["force"]?.Value<bool>() ?? false;

                    if (!RaiseAndWaitForCompletion(20000))
                        throw new TimeoutException("delete_family_type : délai dépassé (20 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"delete_family_type a échoué : {ex.Message}");
                }
            }
        }
    }
}
