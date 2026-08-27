using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Liste les OCCURRENCES de modèles liés (RevitLinkInstance) du document
    /// hôte, avec chemin absolu, statut de chargement et épinglage. Lecture
    /// seule, pas de transaction.
    ///
    /// Comble deux trous constatés le 2026-08-26 : le compteur de catégorie
    /// annonçait 4 liens alors que la vue active n'en montrait que 2, et la
    /// seule commande amont qui lisait chemin et statut (ai_element_filter)
    /// échoue systématiquement.
    /// </summary>
    public class ListLinksEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public bool WaitForCompletion(int timeoutMilliseconds = 15000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            IsSuccess = false;
            ErrorMessage = null;
            Result = null;
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    ErrorMessage = "aucun document actif dans Revit";
                    return;
                }

                var instances = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .OrderBy(i => i.Id.GetValue())
                    .ToList();

                var liens = instances.Select(instance =>
                {
                    var linkType = doc.GetElement(instance.GetTypeId()) as RevitLinkType;
                    var linkDoc = instance.GetLinkDocument();
                    string chemin = LinkUtils.GetAbsolutePath(doc, linkType, out string erreurChemin);
                    return new
                    {
                        linkId = instance.Id.GetValue(),
                        name = instance.Name,
                        typeId = instance.GetTypeId()?.GetValue(),
                        fileName = linkType?.Name,
                        path = chemin,
                        // Pourquoi le chemin manque, plutôt qu'un null muet.
                        pathError = erreurChemin,
                        status = LinkUtils.GetStatus(linkType),
                        // isLoaded : seul critère fiable pour savoir si le
                        // contenu du lien est interrogeable (linkId utilisable).
                        isLoaded = linkDoc != null,
                        linkedProjectName = linkDoc?.Title,
                        pinned = instance.Pinned
                    };
                }).ToList();

                Result = new
                {
                    total = liens.Count,
                    loaded = liens.Count(l => l.isLoaded),
                    links = liens
                };
                IsSuccess = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "ListLinks";
        }
    }
}
