// textNode.cs — put this on any TextMeshPro object that should pull its copy from the CMS.
// The key is built from the hierarchy: <parent name>-<this object's name>, e.g. "Attract-Title".

using TMPro;
using UnityEngine;

namespace SpotCMS
{
    [RequireComponent(typeof(TMP_Text))]
    public class textNode : MonoBehaviour
    {
        [Tooltip("Optional: mirror ANOTHER textNode's copy instead of having its own CMS entry. When " +
                 "set, this node shows the same text as that master node, is NOT an editable CMS key, " +
                 "and is skipped by the exporter. Leave empty for a normal, editable node.")]
        public textNode copySource;

        private TMP_Text _txt;
        public string Key { get; private set; }

        /// <summary>True when this node just mirrors another node's text (no CMS entry of its own).</summary>
        public bool IsCopy => copySource != null;

        /// <summary>This node's own key, built on demand if Awake hasn't run yet.</summary>
        public string ResolvedKey
        {
            get { if (string.IsNullOrEmpty(Key)) Key = BuildKey(); return Key; }
        }

        /// <summary>The key whose text we actually display - follows the copy chain to the ultimate
        /// master (the first non-copy node), so copies-of-copies still resolve to real content.</summary>
        public string MasterKey
        {
            get
            {
                var n = this; int guard = 0;
                while (n.copySource != null && guard++ < 32) n = n.copySource;
                return n.ResolvedKey;
            }
        }

        void Awake()
        {
            _txt = GetComponent<TMP_Text>();
            Key = BuildKey();
        }

   

        void Start()
        {
            if (appAdmin.instance != null)
            {
                appAdmin.instance.Register(this);
                Refresh();
            }
            else
            {
                Debug.LogWarning("[CMS] No appAdmin in scene for " + Key);
            }
          
        }

        void OnDestroy()
        {
            if (appAdmin.instance != null)
                appAdmin.instance.Unregister(this);
        }

        /// <summary>Key is "PageCategory-Field", where the category is the nearest `page` above
        /// this node and the field is the path from that page down to it (so repeated names like
        /// two "Title"s under different containers stay distinct).</summary>
        private string BuildKey() => page.BuildKey(transform);

        /// <summary>Re-pull text for the current language.</summary>
        public void Refresh()
        {
            if (_txt == null) _txt = GetComponent<TMP_Text>();
            if (appAdmin.instance != null)
            {
                _txt.isRightToLeftText = appAdmin.instance.IsCurrentRtl();
                _txt.text = appAdmin.instance.getText(MasterKey);   // own key, or the master's if a copy
            }
        }
    }
}
