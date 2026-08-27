using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Families;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Families
{
    /// <summary>
    /// export_elements_table — une ligne par élément, une colonne par
    /// paramètre demandé, sur l'hôte ou un modèle lié (linkId).
    /// Bornes serrées (25 lignes par défaut, 15 colonnes) : le canal socket
    /// plafonne à ~8 Ko et une ligne coûte cher (décision D4).
    /// </summary>
    public class ExportElementsTableCommand : ExternalEventCommandBase
    {
        private const int DefaultLimit = 25;
        private const int MaxLimit = 100;
        private const int MaxColumns = 15;
        private const int TimeoutMs = 30000;

        private static readonly object _executionLock = new object();
        private ExportElementsTableEventHandler _handler => (ExportElementsTableEventHandler)Handler;

        public override string CommandName => "export_elements_table";

        public ExportElementsTableCommand(UIApplication uiApp)
            : base(new ExportElementsTableEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    string category = parameters?["category"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(category))
                        throw new ArgumentException("category est obligatoire (une table a besoin d'un périmètre)");

                    var noms = parameters?["parameters"]?.ToObject<List<string>>();
                    if (noms == null || noms.Count == 0)
                        throw new ArgumentException("parameters est obligatoire : les colonnes de la table");
                    if (noms.Count > MaxColumns)
                        throw new ArgumentException($"{noms.Count} colonnes demandées, maximum {MaxColumns} " +
                                                    "(limite ~8 Ko du canal socket)");

                    int offset = parameters?["offset"]?.Value<int>() ?? 0;
                    int limit = parameters?["limit"]?.Value<int>() ?? DefaultLimit;
                    if (offset < 0)
                        offset = 0;
                    if (limit < 1)
                        limit = 1;
                    if (limit > MaxLimit)
                        limit = MaxLimit;

                    _handler.LinkId = parameters?["linkId"]?.Value<long>();
                    _handler.Category = category;
                    _handler.ParameterNames = noms;
                    _handler.ExactCategory = parameters?["exactCategory"]?.Value<bool>() ?? false;
                    _handler.TypeFallback = parameters?["typeFallback"]?.Value<bool>() ?? true;
                    _handler.Offset = offset;
                    _handler.Limit = limit;

                    if (!RaiseAndWaitForCompletion(TimeoutMs))
                        throw new TimeoutException("export_elements_table : délai dépassé (30 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"export_elements_table a échoué : {ex.Message}");
                }
            }
        }
    }
}
