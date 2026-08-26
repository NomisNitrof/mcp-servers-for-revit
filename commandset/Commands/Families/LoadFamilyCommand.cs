using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Families;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Families
{
    /// <summary>
    /// load_family — charge un .rfa du disque dans le projet (écrasement si
    /// déjà chargée). Timeout étendu 60 s : un .rfa lourd est lent à charger.
    /// </summary>
    public class LoadFamilyCommand : ExternalEventCommandBase
    {
        private const int TimeoutMs = 60000;

        private static readonly object _executionLock = new object();
        private LoadFamilyEventHandler _handler => (LoadFamilyEventHandler)Handler;

        public override string CommandName => "load_family";

        public LoadFamilyCommand(UIApplication uiApp)
            : base(new LoadFamilyEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    string path = parameters?["path"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(path))
                        throw new ArgumentException("path est obligatoire (chemin local d'un .rfa)");

                    _handler.Path = path;

                    if (!RaiseAndWaitForCompletion(TimeoutMs))
                        throw new TimeoutException("load_family : délai dépassé (60 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"load_family a échoué : {ex.Message}");
                }
            }
        }
    }
}
