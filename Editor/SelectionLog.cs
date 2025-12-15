#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace SeanBud
{
    public class SelectionLog : EditorWindow
    {
        [System.Serializable]
        class SelectedObject
        {
            public bool m_locked = false;
            public Object m_object = null;
            public bool m_inScene = false;
        }

        static readonly int MAX_ITEMS = 50;
        static readonly string STRING_INSCENE = "*";
        static Texture2D _selectionBackground;

        [SerializeField] SelectedObject m_selectedObject;
        [SerializeField] List<SelectedObject> m_selectedObjects = new();
        [SerializeField] Vector2 m_scrollPosition;

        GUIStyle m_lockButtonStyle;
        GUIContent m_searchButtonContent;

        SelectedObject m_dragCandidate;
        Vector2 m_dragStartPos;
        bool m_shouldSelectOnMouseUp;
        int m_clickCountOnMouseDown;

        // Git popup state
        bool m_shouldOpenPopup;
        string m_gitCommand;
        string m_projectPath;
        List<string> m_checkoutPaths;

        [MenuItem("Window/Selection Log")]
        static void ShowWindow() => GetWindow<SelectionLog>("Selection Log");

        void OnEnable() => _selectionBackground = null;

        void OnSelectionChange()
        {
            if (Selection.activeObject == null)
            {
                m_selectedObject = null;
                return;
            }

            if (m_selectedObject == null || m_selectedObject.m_object != Selection.activeObject)
            {
                m_selectedObject = m_selectedObjects.Find(it => it.m_object == Selection.activeObject);

                if (m_selectedObject == null)
                {
                    m_selectedObject = new SelectedObject
                    {
                        m_object = Selection.activeObject,
                        m_inScene = !AssetDatabase.Contains(Selection.activeInstanceID)
                    };
                    InsertInList(m_selectedObject);
                }
                else if (!m_selectedObject.m_locked)
                {
                    m_selectedObjects.Remove(m_selectedObject);
                    InsertInList(m_selectedObject);
                }

                while (m_selectedObjects.Count > MAX_ITEMS)
                    m_selectedObjects.RemoveAt(0);

                Repaint();
            }
        }

        void OnGUI()
        {
            m_scrollPosition = EditorGUILayout.BeginScrollView(m_scrollPosition);
            GUILayout.Space(4);

            bool shownClear = false;
            bool sawLocked = false;

            for (int i = m_selectedObjects.Count - 1; i >= 0; --i)
            {
                var obj = m_selectedObjects[i];

                if (!obj.m_locked && sawLocked && !shownClear)
                {
                    if (LayoutClearButton()) break;
                    shownClear = true;
                }

                if (obj.m_locked) sawLocked = true;

                LayoutItem(i, obj);
            }

            if (!shownClear) LayoutClearButton();
            EditorGUILayout.EndScrollView();

            HandleDragDrop();

            if (m_shouldOpenPopup)
            {
                m_shouldOpenPopup = false;

                GUILayout.Space(8);
                Rect anchorRect = GUILayoutUtility.GetRect(1, 1);
                Vector2 screenPos = GUIUtility.GUIToScreenPoint(anchorRect.position);

                EditorApplication.delayCall += () =>
                {
                    PopupWindow.Show(new Rect(screenPos, Vector2.zero),
                        new GitCheckoutPopup(m_gitCommand, m_projectPath, m_checkoutPaths));
                };
            }
        }

        void HandleDragDrop()
        {
            Event evt = Event.current;
            Rect dropRect = new Rect(0, 0, position.width, position.height);

            bool isDrag = DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0;
            bool isOver = dropRect.Contains(evt.mousePosition);

            if (isDrag && isOver)
            {
                // Always set drag mode + consume event when valid drag
                if (evt.type == EventType.DragUpdated)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    evt.Use();
                }

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        if (obj == null || m_selectedObjects.Exists(o => o.m_object == obj)) continue;

                        var newObj = new SelectedObject
                        {
                            m_object = obj,
                            m_locked = true,
                            m_inScene = !AssetDatabase.Contains(obj)
                        };
                        InsertInList(newObj);
                    }

                    Repaint();
                    evt.Use();
                }

                // Do highlight last (during repaint)
                if (evt.type == EventType.Repaint)
                {
                    Color c = new Color(0.3f, 0.6f, 1f, 0.15f);
                    EditorGUI.DrawRect(dropRect, c);
                }
            }
        }

        bool LayoutClearButton()
        {
            GUILayout.Space(4);
            bool clear = GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Height(20));
            if (clear)
                m_selectedObjects.RemoveAll(it => !it.m_locked);
            GUILayout.Space(4);
            return clear;
        }

        void LayoutItem(int index, SelectedObject obj)
        {
            if (obj?.m_object == null) return;

            // Lazy init styles
            m_lockButtonStyle ??= new GUIStyle("IN LockButton")
            {
                margin = new RectOffset(2, 2, 2, 2),
                fixedHeight = 16
            };

            m_searchButtonContent ??= EditorGUIUtility.IconContent("d_ViewToolZoom");

            GUILayout.BeginHorizontal(GUILayout.Height(18));
            GUI.enabled = true;

            bool wasLocked = obj.m_locked;
            obj.m_locked = GUILayout.Toggle(obj.m_locked, GUIContent.none, m_lockButtonStyle, GUILayout.Width(18));
            if (wasLocked != obj.m_locked)
            {
                m_selectedObjects.Remove(obj);
                InsertInList(obj);
            }

            bool isSelected = obj == m_selectedObject;

            GUIStyle rowStyle = new(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 18,
                padding = new RectOffset(6, 6, 0, 0),
                normal = { textColor = GUI.skin.label.normal.textColor }
            };

            if (isSelected)
            {
                _selectionBackground ??= SolidTex(new Color(0.28f, 0.42f, 0.65f));
                rowStyle.normal.background = _selectionBackground;
                rowStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            }

            Rect rowRect = GUILayoutUtility.GetRect(GUIContent.none, rowStyle, GUILayout.ExpandWidth(true));
            Event evt = Event.current;

            if (!isSelected)
            {
                if (rowRect.Contains(evt.mousePosition)) EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.05f));
                else if (index % 2 == 0) EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.02f));
            }

            if (evt.type == EventType.ContextClick && rowRect.Contains(evt.mousePosition))
            {
                ShowContextMenu(obj);
                evt.Use();
            }

            switch (evt.type)
            {
                case EventType.MouseDown when evt.button == 0 && rowRect.Contains(evt.mousePosition):
                    m_dragCandidate = obj;
                    m_dragStartPos = evt.mousePosition;
                    m_shouldSelectOnMouseUp = true;
                    m_clickCountOnMouseDown = evt.clickCount;
                    break;

                case EventType.MouseDrag when evt.button == 0 && m_dragCandidate == obj:
                    if ((evt.mousePosition - m_dragStartPos).sqrMagnitude > 16f)
                    {
                        StartDrag(obj);
                        m_shouldSelectOnMouseUp = false;
                        m_dragCandidate = null;
                        evt.Use();
                    }
                    break;

                case EventType.MouseUp when m_shouldSelectOnMouseUp && rowRect.Contains(evt.mousePosition):
                    m_selectedObject = obj;

                    if (m_clickCountOnMouseDown == 2)
                    {
                        AssetDatabase.OpenAsset(obj.m_object);
                        GUIUtility.ExitGUI();
                    }
                    else
                    {
                        Selection.activeObject = obj.m_object;
                        evt.Use();
                    }

                    m_shouldSelectOnMouseUp = false;
                    m_dragCandidate = null;
                    break;
            }

            if (isSelected) GUI.Box(rowRect, GUIContent.none, rowStyle);

            GUIContent content = EditorGUIUtility.ObjectContent(obj.m_object, obj.m_object.GetType());
            Rect iconRect = new(rowRect.x + 6, rowRect.y + 1, 16, 16);
            if (content.image) GUI.DrawTexture(iconRect, content.image, ScaleMode.ScaleToFit);

            Rect textRect = new(iconRect.xMax + 2, rowRect.y, rowRect.width - iconRect.width - 40, rowRect.height);
            EditorGUI.LabelField(textRect, obj.m_object.name + (obj.m_inScene ? STRING_INSCENE : ""), rowStyle);

            if (position.width > 30)
            {
                if (GUILayout.Button(m_searchButtonContent, EditorStyles.miniButtonRight, GUILayout.Width(20), GUILayout.Height(18)))
                    EditorGUIUtility.PingObject(obj.m_object);
            }

            GUILayout.EndHorizontal();
        }

        void InsertInList(SelectedObject obj)
        {
            int firstLocked = m_selectedObjects.FindIndex(it => it.m_locked);
            if (firstLocked == -1) m_selectedObjects.Add(obj);
            else m_selectedObjects.Insert(firstLocked, obj);
        }

        void StartDrag(SelectedObject obj)
        {
            if (obj.m_object == null) return;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new[] { obj.m_object };

            string path = AssetDatabase.GetAssetPath(obj.m_object);
            if (!string.IsNullOrEmpty(path))
                DragAndDrop.paths = new[] { path };

            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            DragAndDrop.StartDrag(obj.m_object.name);
            Event.current.Use();
        }

        void ShowContextMenu(SelectedObject obj)
        {
            GenericMenu menu = new();
            string assetPath = AssetDatabase.GetAssetPath(obj.m_object);

            if (!string.IsNullOrEmpty(assetPath))
                menu.AddItem(new GUIContent("Open Containing Folder"), false, () => EditorUtility.RevealInFinder(assetPath));
            else
                menu.AddDisabledItem(new GUIContent("Open Containing Folder"));

            if (!string.IsNullOrEmpty(assetPath))
                menu.AddItem(new GUIContent("Copy Path"), false, () => EditorGUIUtility.systemCopyBuffer = assetPath);

            menu.AddItem(new GUIContent("Copy Name"), false, () => EditorGUIUtility.systemCopyBuffer = obj.m_object.name);

            if (obj.m_locked)
            {
                menu.AddSeparator("");
                int currentIndex = m_selectedObjects.IndexOf(obj);

                // ↑ Move Up
                if (currentIndex < m_selectedObjects.Count - 1 && m_selectedObjects[currentIndex + 1].m_locked)
                    menu.AddItem(new GUIContent("↑ Move Up"), false, () =>
                    {
                        m_selectedObjects.RemoveAt(currentIndex);
                        m_selectedObjects.Insert(currentIndex + 1, obj);
                    });
                else
                    menu.AddDisabledItem(new GUIContent("↑ Move Up"));

                // ↓ Move Down
                if (currentIndex > 0 && m_selectedObjects[currentIndex - 1].m_locked)
                    menu.AddItem(new GUIContent("↓ Move Down"), false, () =>
                    {
                        m_selectedObjects.RemoveAt(currentIndex);
                        m_selectedObjects.Insert(currentIndex - 1, obj);
                    });
                else
                    menu.AddDisabledItem(new GUIContent("↓ Move Down"));

                // Divider before ⇡ Move to Top
                menu.AddSeparator("");

                // ⇡ Move to Top of locked list (visually top = list end)
                int lastLocked = m_selectedObjects.FindLastIndex(it => it.m_locked);
                if (lastLocked != -1 && currentIndex != lastLocked)
                    menu.AddItem(new GUIContent("⇡ Move to Top"), false, () =>
                    {
                        m_selectedObjects.RemoveAt(currentIndex);
                        int insertIndex = Mathf.Min(m_selectedObjects.Count, lastLocked + 1);
                        m_selectedObjects.Insert(insertIndex, obj);
                    });
                else
                    menu.AddDisabledItem(new GUIContent("⇡ Move to Top"));



            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Git/Checkout from main"), false, () =>
            {
                if (string.IsNullOrEmpty(assetPath)) return;

                string projectPath = Application.dataPath.Replace("/Assets", "");
                List<string> paths = new() { $"\"{assetPath}\"" };

                string metaAssetPath = assetPath + ".meta";
                string metaFullPath = System.IO.Path.Combine(projectPath, metaAssetPath);
                if (System.IO.File.Exists(metaFullPath))
                    paths.Add($"\"{metaAssetPath}\"");

                string gitCommand = $"checkout origin/main -- {string.Join(" ", paths)}";

                m_gitCommand = gitCommand;
                m_projectPath = projectPath;
                m_checkoutPaths = paths;
                m_shouldOpenPopup = true;
            });

            menu.AddItem(new GUIContent("Git/Checkout from SHA..."), false, () =>
            {
                if (string.IsNullOrEmpty(assetPath)) return;

                string projectPath = Application.dataPath.Replace("/Assets", "");
                List<string> paths = new() { $"\"{assetPath}\"" };

                string metaAssetPath = assetPath + ".meta";
                string metaFullPath = System.IO.Path.Combine(projectPath, metaAssetPath);
                if (System.IO.File.Exists(metaFullPath))
                    paths.Add($"\"{metaAssetPath}\"");

                m_gitCommand = null; // null = SHA mode
                m_projectPath = projectPath;
                m_checkoutPaths = paths;
                m_shouldOpenPopup = true;
            });

            menu.ShowAsContext();
        }

        static Texture2D SolidTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }

    class GitCheckoutPopup : PopupWindowContent
    {
        readonly string m_command;
        readonly string m_workingDir;
        readonly List<string> m_paths;
        string m_shaInput = "";
        string m_error = null;
        string m_output = null;
        Vector2 m_outputScroll;

        public GitCheckoutPopup(string command, string workingDir, List<string> paths)
        {
            m_command = command;
            m_workingDir = workingDir;
            m_paths = paths;
        }

        public override Vector2 GetWindowSize()
        {
            float height = 160 + 20 * Mathf.Max(1, m_paths.Count);
            return new Vector2(500, height);
        }

        public override void OnGUI(Rect rect)
        {
            string source = string.IsNullOrEmpty(m_command) ? "a specific commit (SHA)" : "the 'main' branch";

            GUILayout.Label($"Checkout from {source}", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"This will overwrite the following files with the version from {source}. This action cannot be undone.",
                MessageType.Warning
            );

            foreach (var p in m_paths)
                EditorGUILayout.LabelField(p.Trim('"'), EditorStyles.textField);

            if (string.IsNullOrEmpty(m_command))
            {
                GUILayout.Label("Enter commit SHA:");
                m_shaInput = EditorGUILayout.TextField(m_shaInput);
            }

            string finalCommand = m_command ?? $"checkout {m_shaInput} -- {string.Join(" ", m_paths)}";

            if (GUILayout.Button("Run Git Command"))
            {
                try
                {
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "git",
                            Arguments = finalCommand,
                            WorkingDirectory = m_workingDir,
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };

                    process.Start();
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    AssetDatabase.Refresh();

                    m_output = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
                    m_error = string.IsNullOrWhiteSpace(stderr) ? null : stderr;

                    editorWindow.Repaint();
                }
                catch (System.Exception ex)
                {
                    m_error = ex.Message;
                    editorWindow.Repaint();
                }
            }

            if (!string.IsNullOrEmpty(m_error))
            {
                EditorGUILayout.HelpBox(m_error, MessageType.Error);
            }

            if (!string.IsNullOrEmpty(m_output))
            {
                GUILayout.Label("Git Output:", EditorStyles.miniBoldLabel);
                m_outputScroll = EditorGUILayout.BeginScrollView(m_outputScroll, GUILayout.Height(80));
                EditorGUILayout.SelectableLabel(m_output, EditorStyles.textArea, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }
    }
}
#endif
