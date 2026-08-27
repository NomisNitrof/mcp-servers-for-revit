using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Helpers pour les modèles liés (RevitLinkInstance / RevitLinkType) :
    /// résolution du document cible (hôte ou lié) et extraction des métadonnées
    /// de lien (chemin absolu, statut de chargement).
    ///
    /// Rappel API : un document lié est accessible en LECTURE SEULE depuis
    /// l'hôte — aucune transaction n'y est permise. Les commandes d'écriture
    /// n'acceptent donc jamais de linkId.
    /// </summary>
    public static class LinkUtils
    {
        /// <summary>
        /// Résout le document à interroger : l'hôte si linkId est absent, sinon
        /// le document du lien désigné. Renvoie false et remplit error si la
        /// résolution échoue (id inconnu, pas un lien, lien non chargé).
        /// </summary>
        public static bool TryResolveDocument(
            UIApplication app, long? linkId, out Document doc, out string error)
        {
            doc = null;
            error = null;

            var hostDoc = app.ActiveUIDocument?.Document;
            if (hostDoc == null)
            {
                error = "aucun document actif dans Revit";
                return false;
            }

            if (!linkId.HasValue)
            {
                doc = hostDoc;
                return true;
            }

            var instance = hostDoc.GetElement(FamilyParameterUtils.IdFromValue(linkId.Value))
                as RevitLinkInstance;
            if (instance == null)
            {
                error = $"aucune occurrence de lien Revit (RevitLinkInstance) avec l'id {linkId.Value} — " +
                        "utiliser list_links pour obtenir les ids valides";
                return false;
            }

            var linkDoc = instance.GetLinkDocument();
            if (linkDoc == null)
            {
                error = $"le lien {linkId.Value} n'est pas chargé en mémoire (déchargé, introuvable " +
                        "ou non résolu) — son contenu est inaccessible ; voir status dans list_links";
                return false;
            }

            doc = linkDoc;
            return true;
        }

        /// <summary>
        /// Variante levante, pour les handlers amont dont la gestion d'erreur
        /// passe par le catch (résultat typé Success/Message) plutôt que par
        /// un champ ErrorMessage.
        /// </summary>
        public static Document ResolveDocumentOrThrow(UIApplication app, long? linkId)
        {
            if (!TryResolveDocument(app, linkId, out Document doc, out string error))
                throw new InvalidOperationException(error);
            return doc;
        }

        /// <summary>Chemin absolu visible du fichier lié, ou null si indisponible.</summary>
        public static string GetAbsolutePath(RevitLinkType linkType)
        {
            try
            {
                ExternalFileReference reference = linkType?.GetExternalFileReference();
                if (reference == null)
                    return null;
                return ModelPathUtils.ConvertModelPathToUserVisiblePath(reference.GetAbsolutePath());
            }
            catch
            {
                // Lien jamais résolu ou référence externe absente : pas de chemin.
                return null;
            }
        }

        /// <summary>Statut de chargement du lien sous forme de chaîne, ou null.</summary>
        public static string GetStatus(RevitLinkType linkType)
        {
            try
            {
                return linkType?.GetLinkedFileStatus().ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
