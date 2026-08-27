using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Families;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Families
{
    /// <summary>
    /// list_elements — énumère ou compte les éléments placés d'un document
    /// (hôte ou modèle lié via linkId). Timeout étendu 30 s : un modèle lié
    /// volumineux est long à parcourir.
    /// </summary>
    public class ListElementsCommand : ExternalEventCommandBase
    {
        private const int DefaultLimit = 50;
        private const int MaxLimit = 200;
        private const int TimeoutMs = 30000;

        private static readonly object _executionLock = new object();
        private ListElementsEventHandler _handler => (ListElementsEventHandler)Handler;

        public override string CommandName => "list_elements";

        public ListElementsCommand(UIApplication uiApp)
            : base(new ListElementsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int offset = parameters?["offset"]?.Value<int>() ?? 0;
                    int limit = parameters?["limit"]?.Value<int>() ?? DefaultLimit;
                    if (offset < 0)
                        offset = 0;
                    if (limit < 1)
                        limit = 1;
                    if (limit > MaxLimit)
                        limit = MaxLimit;

                    _handler.LinkId = parameters?["linkId"]?.Value<long>();
                    _handler.Category = parameters?["category"]?.Value<string>();
                    _handler.Summarize = parameters?["summarize"]?.Value<bool>() ?? false;
                    _handler.Offset = offset;
                    _handler.Limit = limit;

                    if (!RaiseAndWaitForCompletion(TimeoutMs))
                        throw new TimeoutException("list_elements : délai dépassé (30 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"list_elements a échoué : {ex.Message}");
                }
            }
        }
    }
}
