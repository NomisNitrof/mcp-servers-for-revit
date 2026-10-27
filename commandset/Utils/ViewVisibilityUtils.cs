using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Vue visée par une écriture de visibilité : la vue demandée, et la vue
    /// qui sera réellement modifiée (la même, ou son gabarit si force).
    /// </summary>
    public class CibleVue
    {
        public View Demandee { get; set; }
        public View Ecriture { get; set; }
        public bool GabaritModifie => Ecriture.Id != Demandee.Id;
    }

    /// <summary>
    /// Résolution de la vue, garde-fou des gabarits et résolution des
    /// catégories pour les commandes de visibilité (set_link_visibility,
    /// set_category_visibility, get_view_visibility).
    /// </summary>
    public static class ViewVisibilityUtils
    {
        /// <summary>
        /// Vue visée : viewId si fourni, sinon la vue active du document actif.
        /// </summary>
        public static bool TryResolveView(
            UIApplication app, long? viewId, out Document doc, out View view, out string error)
        {
            doc = null;
            view = null;
            error = null;

            var uiDoc = app.ActiveUIDocument;
            if (uiDoc == null)
            {
                error = "aucun document actif dans Revit";
                return false;
            }
            doc = uiDoc.Document;

            if (!viewId.HasValue)
            {
                view = uiDoc.ActiveView;
                if (view == null)
                {
                    error = "aucune vue active dans Revit";
                    return false;
                }
                return true;
            }

            view = doc.GetElement(FamilyParameterUtils.IdFromValue(viewId.Value)) as View;
            if (view == null)
            {
                error = $"aucune vue (View) avec l'id {viewId.Value}";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Le gabarit de la vue verrouille-t-il ce volet de Visibilité/Graphismes ?
        /// Vrai si le gabarit liste le paramètre parmi ceux qu'il contrôle
        /// (GetTemplateParameterIds) et ne le marque pas « non inclus »
        /// (GetNonControlledTemplateParameterIds) — c'est la case à cocher
        /// du dialogue « Propriétés du gabarit de vue ».
        /// </summary>
        public static bool GabaritControle(View gabarit, BuiltInParameter volet)
        {
            var id = new ElementId(volet);
            bool liste = gabarit.GetTemplateParameterIds().Any(p => p.GetValue() == id.GetValue());
            if (!liste)
                return false;
            return !gabarit.GetNonControlledTemplateParameterIds().Any(p => p.GetValue() == id.GetValue());
        }

        /// <summary>
        /// Détermine la vue à modifier. Vue sans gabarit, ou gabarit qui ne
        /// contrôle pas ce volet : la vue elle-même. Gabarit qui le contrôle :
        /// refus net (« vue pilotée par un gabarit : nom »), sauf force — la
        /// modification porte alors sur le GABARIT, donc sur toutes les vues
        /// qui l'utilisent.
        /// </summary>
        public static bool TryResolveWriteTarget(
            Document doc, View vue, BuiltInParameter volet, bool force,
            out CibleVue cible, out string error)
        {
            cible = null;
            error = null;

            if (!vue.AreGraphicsOverridesAllowed())
            {
                error = $"la vue « {vue.Name} » ({vue.ViewType}) ne supporte pas Visibilité/Graphismes";
                return false;
            }

            View ecriture = vue;
            if (!vue.IsTemplate && vue.ViewTemplateId != ElementId.InvalidElementId)
            {
                var gabarit = doc.GetElement(vue.ViewTemplateId) as View;
                if (gabarit != null && GabaritControle(gabarit, volet))
                {
                    if (!force)
                    {
                        error = $"vue pilotée par un gabarit : {gabarit.Name} — rien n'a été modifié. " +
                                "force: true modifierait le GABARIT (donc toutes les vues qui l'utilisent) ; " +
                                "sinon retirer le gabarit de la vue ou le déverrouiller dans Revit.";
                        return false;
                    }
                    ecriture = gabarit;
                }
            }

            cible = new CibleVue { Demandee = vue, Ecriture = ecriture };
            return true;
        }

        /// <summary>
        /// Volet de Visibilité/Graphismes qui gouverne une catégorie.
        /// </summary>
        public static BuiltInParameter VoletDeCategorie(Category categorie)
        {
            switch (categorie.CategoryType)
            {
                case CategoryType.Annotation:
                    return BuiltInParameter.VIS_GRAPHICS_ANNOTATION;
                case CategoryType.AnalyticalModel:
                    return BuiltInParameter.VIS_GRAPHICS_ANALYTICAL_MODEL;
                default:
                    return BuiltInParameter.VIS_GRAPHICS_MODEL;
            }
        }

        /// <summary>
        /// Catégorie par son nom affiché (langue du modèle). Même règle que
        /// list_elements : sous-chaîne insensible à la casse, ou égalité
        /// stricte si exact. En mode sous-chaîne, un nom égal à une catégorie
        /// l'emporte sur les autres correspondances (« Murs » ne devient pas
        /// ambigu avec « Murs-rideaux »). Plusieurs correspondances restantes
        /// = erreur qui les énumère.
        /// </summary>
        public static bool TryResolveCategory(
            Document doc, string nom, bool exact, out Category categorie, out string error)
        {
            categorie = null;
            error = null;

            var candidates = new List<Category>();
            foreach (Category c in doc.Settings.Categories)
            {
                if (c == null || c.CategoryType == CategoryType.Internal || c.CategoryType == CategoryType.Invalid)
                    continue;
                candidates.Add(c);
            }

            var egales = candidates
                .Where(c => string.Equals(c.Name, nom, StringComparison.OrdinalIgnoreCase)).ToList();
            if (egales.Count == 1)
            {
                categorie = egales[0];
                return true;
            }

            var trouvees = egales.Count > 1
                ? egales
                : exact
                    ? new List<Category>()
                    : candidates.Where(c => c.Name.IndexOf(nom, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (trouvees.Count == 1)
            {
                categorie = trouvees[0];
                return true;
            }

            if (trouvees.Count == 0)
            {
                error = exact
                    ? $"aucune catégorie nommée exactement « {nom} » (nom affiché, langue du modèle)"
                    : $"aucune catégorie dont le nom contient « {nom} » (nom affiché, langue du modèle)";
                return false;
            }

            var noms = string.Join(", ", trouvees.Select(c => $"« {c.Name} »").Take(12));
            error = $"catégorie ambiguë : « {nom} » correspond à {trouvees.Count} catégories ({noms}" +
                    (trouvees.Count > 12 ? ", …" : "") + ") — préciser le nom ou passer exactCategory: true";
            return false;
        }

        /// <summary>
        /// Écriture dans une transaction nommée. Toute exception annule la
        /// transaction avant de remonter : la vue reste dans son état d'origine.
        /// </summary>
        public static void Ecrire(Document doc, string nomTransaction, Action action)
        {
            using (var transaction = new Transaction(doc, nomTransaction))
            {
                transaction.Start();
                try
                {
                    action();
                    transaction.Commit();
                }
                catch
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                        transaction.RollBack();
                    throw;
                }
            }
        }

        /// <summary>
        /// Identité d'une vue pour les réponses.
        /// </summary>
        public static object DecrireVue(Document doc, View vue)
        {
            View gabarit = null;
            if (!vue.IsTemplate && vue.ViewTemplateId != ElementId.InvalidElementId)
                gabarit = doc.GetElement(vue.ViewTemplateId) as View;

            return new
            {
                viewId = vue.Id.GetValue(),
                name = vue.Name,
                viewType = vue.ViewType.ToString(),
                isTemplate = vue.IsTemplate,
                hasTemplate = gabarit != null,
                templateId = gabarit?.Id.GetValue(),
                templateName = gabarit?.Name
            };
        }
    }
}
