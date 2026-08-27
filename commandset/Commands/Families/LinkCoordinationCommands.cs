using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Families;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Families
{
    /// <summary>
    /// get_link_transform — transformation hôte ↔ lien (origine + base).
    /// </summary>
    public class GetLinkTransformCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetLinkTransformEventHandler _handler => (GetLinkTransformEventHandler)Handler;

        public override string CommandName => "get_link_transform";

        public GetLinkTransformCommand(UIApplication uiApp)
            : base(new GetLinkTransformEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    long? linkId = parameters?["linkId"]?.Value<long>();
                    if (!linkId.HasValue)
                        throw new ArgumentException("linkId est obligatoire");

                    _handler.LinkId = linkId.Value;

                    if (!RaiseAndWaitForCompletion(15000))
                        throw new TimeoutException("get_link_transform : délai dépassé (15 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"get_link_transform a échoué : {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// list_link_levels_grids — niveaux et quadrillages d'un lien (ou de l'hôte).
    /// </summary>
    public class ListLinkLevelsGridsCommand : ExternalEventCommandBase
    {
        private const int DefaultLimit = 60;
        private const int MaxLimit = 200;

        private static readonly object _executionLock = new object();
        private ListLinkLevelsGridsEventHandler _handler => (ListLinkLevelsGridsEventHandler)Handler;

        public override string CommandName => "list_link_levels_grids";

        public ListLinkLevelsGridsCommand(UIApplication uiApp)
            : base(new ListLinkLevelsGridsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int limit = parameters?["limit"]?.Value<int>() ?? DefaultLimit;
                    if (limit < 1)
                        limit = 1;
                    if (limit > MaxLimit)
                        limit = MaxLimit;

                    _handler.LinkId = parameters?["linkId"]?.Value<long>();
                    _handler.Limit = limit;

                    if (!RaiseAndWaitForCompletion(15000))
                        throw new TimeoutException("list_link_levels_grids : délai dépassé (15 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"list_link_levels_grids a échoué : {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// copy_elements_from_link — copie d'éléments d'un lien vers l'hôte.
    /// Timeout étendu 60 s : la copie déclenche la duplication des types.
    /// </summary>
    public class CopyElementsFromLinkCommand : ExternalEventCommandBase
    {
        private const int MaxElements = 200;
        private const int TimeoutMs = 60000;

        private static readonly object _executionLock = new object();
        private CopyElementsFromLinkEventHandler _handler => (CopyElementsFromLinkEventHandler)Handler;

        public override string CommandName => "copy_elements_from_link";

        public CopyElementsFromLinkCommand(UIApplication uiApp)
            : base(new CopyElementsFromLinkEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    long? linkId = parameters?["linkId"]?.Value<long>();
                    if (!linkId.HasValue)
                        throw new ArgumentException("linkId est obligatoire (le lien source)");

                    var ids = parameters?["elementIds"]?.ToObject<List<long>>();
                    if (ids == null || ids.Count == 0)
                        throw new ArgumentException("elementIds est obligatoire et non vide " +
                                                    "(ids DANS le document lié)");
                    if (ids.Count > MaxElements)
                        throw new ArgumentException($"{ids.Count} éléments demandés, maximum {MaxElements} " +
                                                    "par appel");

                    _handler.LinkId = linkId.Value;
                    _handler.ElementIds = ids;

                    if (!RaiseAndWaitForCompletion(TimeoutMs))
                        throw new TimeoutException("copy_elements_from_link : délai dépassé (60 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"copy_elements_from_link a échoué : {ex.Message}");
                }
            }
        }
    }
}
