using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// get_link_transform — transformation totale d'une occurrence de lien :
    /// origine et vecteurs de base permettant de passer des coordonnées du
    /// document lié à celles de l'hôte. Nécessaire dès qu'on compare des
    /// positions entre deux documents.
    /// </summary>
    public class GetLinkTransformEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public long LinkId { get; set; }

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

                var instance = doc.GetElement(FamilyParameterUtils.IdFromValue(LinkId)) as RevitLinkInstance;
                if (instance == null)
                {
                    ErrorMessage = $"aucune occurrence de lien avec l'id {LinkId} — voir list_links";
                    return;
                }

                Transform totale = instance.GetTotalTransform();

                Result = new
                {
                    linkId = LinkId,
                    name = instance.Name,
                    // Unités internes Revit (pieds).
                    origin = new { x = totale.Origin.X, y = totale.Origin.Y, z = totale.Origin.Z },
                    basisX = new { x = totale.BasisX.X, y = totale.BasisX.Y, z = totale.BasisX.Z },
                    basisY = new { x = totale.BasisY.X, y = totale.BasisY.Y, z = totale.BasisY.Z },
                    basisZ = new { x = totale.BasisZ.X, y = totale.BasisZ.Y, z = totale.BasisZ.Z },
                    isIdentity = totale.IsIdentity,
                    isTranslation = totale.IsTranslation
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

        public string GetName() => "GetLinkTransform";
    }

    /// <summary>
    /// list_link_levels_grids — niveaux et quadrillages d'un modèle lié (ou de
    /// l'hôte). Cas particulier assez fréquent pour mériter son outil : c'est
    /// ce qu'on consulte avant de reprendre les repères d'un consultant.
    /// Élévations en unités internes Revit, dans le repère du document
    /// interrogé (voir get_link_transform pour passer au repère hôte).
    /// </summary>
    public class ListLinkLevelsGridsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public long? LinkId { get; set; }
        public int Limit { get; set; }

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
                if (!LinkUtils.TryResolveDocument(app, LinkId, out Document doc, out string erreurDoc))
                {
                    ErrorMessage = erreurDoc;
                    return;
                }

                var niveaux = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Take(Limit)
                    .Select(l => new
                    {
                        elementId = l.Id.GetValue(),
                        name = l.Name,
                        elevation = l.Elevation,
                        elevationDisplay = ElevationAffichee(doc, l)
                    })
                    .ToList();

                var quadrillages = new FilteredElementCollector(doc)
                    .OfClass(typeof(Grid))
                    .Cast<Grid>()
                    .OrderBy(g => g.Name, StringComparer.Ordinal)
                    .Take(Limit)
                    .Select(g => new
                    {
                        elementId = g.Id.GetValue(),
                        name = g.Name
                    })
                    .ToList();

                Result = new
                {
                    documentTitle = doc.Title,
                    linkId = LinkId,
                    levelCount = niveaux.Count,
                    gridCount = quadrillages.Count,
                    levels = niveaux,
                    grids = quadrillages
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

        private static string ElevationAffichee(Document doc, Level l)
        {
            try
            {
                return l.get_Parameter(BuiltInParameter.LEVEL_ELEV)?.AsValueString();
            }
            catch { return null; }
        }

        public string GetName() => "ListLinkLevelsGrids";
    }

    /// <summary>
    /// copy_elements_from_link — copie des éléments d'un modèle lié vers le
    /// document hôte, en appliquant la transformation du lien pour qu'ils
    /// atterrissent au bon endroit. Le geste classique : reprendre niveaux et
    /// quadrillages d'un consultant.
    ///
    /// La transaction porte sur l'HÔTE (autorisé) ; le document lié n'est que
    /// la source, jamais modifié. Les conflits de noms de types sont résolus
    /// automatiquement en faveur de l'hôte — sans quoi Revit ouvrirait une
    /// boîte de dialogue modale, qui gèlerait tous les ExternalEvent.
    /// </summary>
    public class CopyElementsFromLinkEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public long LinkId { get; set; }
        public List<long> ElementIds { get; set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        /// <summary>Résout les collisions de noms de types sans dialogue modal.</summary>
        private class UtiliserTypesDeDestination : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                return DuplicateTypeAction.UseDestinationTypes;
            }
        }

        public void Execute(UIApplication app)
        {
            IsSuccess = false;
            ErrorMessage = null;
            Result = null;
            try
            {
                var hostDoc = app.ActiveUIDocument?.Document;
                if (hostDoc == null)
                {
                    ErrorMessage = "aucun document actif dans Revit";
                    return;
                }

                var instance = hostDoc.GetElement(FamilyParameterUtils.IdFromValue(LinkId)) as RevitLinkInstance;
                if (instance == null)
                {
                    ErrorMessage = $"aucune occurrence de lien avec l'id {LinkId} — voir list_links";
                    return;
                }

                var linkDoc = instance.GetLinkDocument();
                if (linkDoc == null)
                {
                    ErrorMessage = $"le lien {LinkId} n'est pas chargé : impossible d'en copier des éléments";
                    return;
                }

                // Valider les ids AVANT la transaction : un id absent doit
                // donner un message net, pas une exception opaque de l'API.
                var ids = new List<ElementId>();
                var introuvables = new List<long>();
                foreach (var valeur in ElementIds)
                {
                    var id = FamilyParameterUtils.IdFromValue(valeur);
                    if (linkDoc.GetElement(id) == null)
                        introuvables.Add(valeur);
                    else
                        ids.Add(id);
                }
                if (introuvables.Count > 0)
                {
                    ErrorMessage = "éléments introuvables dans le lien : " +
                                   string.Join(", ", introuvables) +
                                   " — les ids doivent être ceux du DOCUMENT LIÉ " +
                                   "(list_elements avec linkId, ou list_link_levels_grids)";
                    return;
                }

                var options = new CopyPasteOptions();
                options.SetDuplicateTypeNamesHandler(new UtiliserTypesDeDestination());

                ICollection<ElementId> nouveaux;
                using (var transaction = new Transaction(hostDoc, "MCP — Copier des éléments depuis un lien"))
                {
                    transaction.Start();
                    nouveaux = ElementTransformUtils.CopyElements(
                        linkDoc, ids, hostDoc, instance.GetTotalTransform(), options);
                    transaction.Commit();
                }

                Result = new
                {
                    linkId = LinkId,
                    sourceDocument = linkDoc.Title,
                    requested = ElementIds.Count,
                    copied = nouveaux?.Count ?? 0,
                    newElementIds = nouveaux?.Select(id => id.GetValue()).Take(100).ToList()
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

        public string GetName() => "CopyElementsFromLink";
    }
}
