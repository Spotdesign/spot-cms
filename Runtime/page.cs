// page.cs — marks a parent object as a CMS "page" (the first half of a key, e.g. "Attract").
//
// Put it on the logical container. Any textNode nested underneath — however deep — resolves
// its category from the nearest page above it, and its FIELD from the path between the page and
// the node. That path disambiguates repeated names, so two objects both called "Title" under
// different sub-containers get distinct keys without any per-field setup.
//
//   Attractor              <- page
//     Left panel
//       Title              <- textNode  ->  "Attractor-Left panel/Title"
//     Start button
//       Title              <- textNode  ->  "Attractor-Start button/Title"
//     Heading              <- textNode  ->  "Attractor-Heading"   (direct child)
//
// The key stays two-level (page - field); the field simply carries the sub-path joined with '/'.
// Names must not contain '-' (that's the page/field separator).

using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SpotCMS
{
    public class page : MonoBehaviour
    {
        /// <summary>Category used for fields that aren't under any page (e.g. a main title).</summary>
        public const string GlobalCategory = "Global";

        [Tooltip("Category name (cats[0]). Leave blank to use this GameObject's name.")]
        public string pageName = "";

        public string Category => string.IsNullOrWhiteSpace(pageName) ? name : pageName;

        // Naming flags used by the fade system that should NOT appear in CMS keys/labels,
        // e.g. "Title del[0.6]" or "Panel ignore". Strips del[...] and a standalone 'ignore'.
        public static string CleanName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var s = Regex.Replace(raw, @"del\s*\[[^\]]*\]", "", RegexOptions.IgnoreCase); // del[0.6]
            s = Regex.Replace(s, @"\bignore\b", "", RegexOptions.IgnoreCase);             // ignore
            s = Regex.Replace(s, @"\s+", " ");                                            // collapse spaces
            return s.Trim();
        }

        /// <summary>
        /// Resolve the page category and field for a node. Field is the '/'-joined path from the
        /// page down to the node (disambiguating repeated names). Falls back to the immediate
        /// parent's name if there is no page above. Returns false if no category can be found.
        /// </summary>
        public static bool Resolve(Transform t, out string category, out string field)
        {
            category = "";
            field = t != null ? CleanName(t.name) : "";
            if (t == null) return false;

            var p = t.GetComponentInParent<page>(true);
            if (p != null)
            {
                category = CleanName(p.Category);
                field = RelativePath(p.transform, t);
                if (string.IsNullOrEmpty(field)) field = CleanName(t.name);
                return !string.IsNullOrEmpty(category);
            }

            // No page above -> this is a GLOBAL field (data.json category "Global").
            category = GlobalCategory;
            field = CleanName(t.name);
            return true;
        }

        /// <summary>"Category-field" for a node, or just the node name if nothing resolves.</summary>
        public static string BuildKey(Transform t)
            => Resolve(t, out var category, out var field) ? category + "-" + field : (t != null ? t.name : "");

        // Path of GameObject names from just under `root` down to `t`, joined with '/'.
        private static string RelativePath(Transform root, Transform t)
        {
            var names = new List<string>();
            var cur = t;
            while (cur != null && cur != root)
            {
                var clean = CleanName(cur.name);
                if (!string.IsNullOrEmpty(clean)) names.Add(clean); // drop segments that were only flags
                cur = cur.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
