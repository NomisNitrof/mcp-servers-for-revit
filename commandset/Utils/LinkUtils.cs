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

        /// <summary>
        /// Chemin absolu visible du fichier lié, ou null si indisponible —
        /// avec la RAISON dans erreur plutôt qu'un null muet.
        ///
        /// Constaté le 2026-08-27 : sur quatre liens pourtant « Loaded », la
        /// méthode d'instance GetExternalFileReference() ne rendait rien. On
        /// passe donc d'abord par ExternalFileUtils, qui teste l'éligibilité
        /// avant d'interroger, puis on retombe sur la méthode d'instance.
        /// </summary>
        public static string GetAbsolutePath(Document hostDoc, RevitLinkType linkType, out string erreur)
        {
            erreur = null;
            if (linkType == null)
            {
                erreur = "type de lien (RevitLinkType) introuvable";
                return null;
            }

            // Voie 1 : utilitaire de document, avec test d'éligibilité.
            try
            {
                if (hostDoc != null && ExternalFileUtils.IsExternalFileReference(hostDoc, linkType.Id))
                {
                    string chemin = CheminVisible(
                        ExternalFileUtils.GetExternalFileReference(hostDoc, linkType.Id));
                    if (!string.IsNullOrEmpty(chemin))
                        return chemin;
                }
            }
            catch (Exception ex)
            {
                erreur = "ExternalFileUtils : " + ex.Message;
            }

            // Voie 2 : méthode d'instance.
            try
            {
                string chemin = CheminVisible(linkType.GetExternalFileReference());
                if (!string.IsNullOrEmpty(chemin))
                {
                    erreur = null;
                    return chemin;
                }
                if (erreur == null)
                    erreur = "aucune référence de fichier externe exploitable";
            }
            catch (Exception ex)
            {
                erreur = (erreur == null ? "" : erreur + " ; ") +
                         "GetExternalFileReference : " + ex.Message;
            }

            // Voie 3 : ressource externe. Constaté le 2026-08-27 : un modèle
            // hébergé dans le nuage (ACC / BIM 360) n'est PAS un fichier
            // externe — « This Element does not represent an external file » —
            // et son emplacement ne se lit que par ce chemin-là.
            try
            {
                var ressources = linkType.GetExternalResourceReferences();
                if (ressources != null)
                {
                    foreach (var couple in ressources)
                    {
                        var reference = couple.Value;
                        if (reference == null)
                            continue;
                        if (!string.IsNullOrWhiteSpace(reference.InSessionPath))
                        {
                            erreur = null;
                            return reference.InSessionPath;
                        }
                        // À défaut, la fiche d'identification de la ressource
                        // (projet et modèle nuagiques) vaut mieux que rien.
                        var infos = reference.GetReferenceInformation();
                        if (infos != null && infos.Count > 0)
                        {
                            erreur = null;
                            return string.Join(" | ", infos.Select(kv => kv.Key + "=" + kv.Value));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                erreur = (erreur == null ? "" : erreur + " ; ") +
                         "GetExternalResourceReferences : " + ex.Message;
            }
            return null;
        }

        private static string CheminVisible(ExternalFileReference reference)
        {
            if (reference == null)
                return null;
            ModelPath chemin = reference.GetAbsolutePath();
            if (chemin == null)
                return null;
            string visible = ModelPathUtils.ConvertModelPathToUserVisiblePath(chemin);
            return string.IsNullOrWhiteSpace(visible) ? null : visible;
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
