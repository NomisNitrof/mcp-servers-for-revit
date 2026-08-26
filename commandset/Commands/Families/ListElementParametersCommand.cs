using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Families;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Families
{
    /// <summary>
    /// list_element_parameters — liste paginée des paramètres d'occurrence
    /// d'un élément placé (le pendant instance de list_family_type_parameters).
    /// Pagination (offset/limit) pour rester sous ~8 Ko par réponse (décision D4).
    /// </summary>
    public class ListElementParametersCommand : ExternalEventCommandBase
    {
        private const int DefaultLimit = 30;
        private const int MaxLimit = 100;

        private static readonly object _executionLock = new object();
        private ListElementParametersEventHandler _handler => (ListElementParametersEventHandler)Handler;

        public override string CommandName => "list_element_parameters";

        public ListElementParametersCommand(UIApplication uiApp)
            : base(new ListElementParametersEventHandler(), uiApp)
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

                    int offset = parameters?["offset"]?.Value<int>() ?? 0;
                    int limit = parameters?["limit"]?.Value<int>() ?? DefaultLimit;
                    if (offset < 0)
                        offset = 0;
                    if (limit < 1)
                        limit = 1;
                    if (limit > MaxLimit)
                        limit = MaxLimit;

                    _handler.ElementId = elementId.Value;
                    _handler.Offset = offset;
                    _handler.Limit = limit;

                    if (!RaiseAndWaitForCompletion(15000))
                        throw new TimeoutException("list_element_parameters : délai dépassé (15 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"list_element_parameters a échoué : {ex.Message}");
                }
            }
        }
    }
}
