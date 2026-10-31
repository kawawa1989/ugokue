using System;
using System.IO;
using Ugokue.App.Rig;
using UnityEditor;
using UnityEngine;

namespace Ugokue.Editor
{
    /// <summary>
    /// エディタで再生したときに読み込むモデルを、固定テストモデル、<c>ugokue-pipeline keyforms</c> が書いたモデル、任意のフォルダから選ぶウィンドウ。
    /// </summary>
    public sealed class ModelSelectorWindow : EditorWindow
    {
        private Vector2 scroll;

        /// <summary>
        /// ウィンドウを開く。
        /// </summary>
        [MenuItem("Ugokue/Model Selector")]
        public static void Open()
        {
            GetWindow<ModelSelectorWindow>("Model Selector");
        }

        /// <summary>
        /// 読み込むモデルのフォルダを選択として保存し、再生中ならその場で読み込み直す。空なら選択を外す（固定テストモデルに戻る）。
        /// </summary>
        public static void Select(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                EditorPrefs.DeleteKey(ModelLocation.EditorSelectionKey);
            }
            else
            {
                EditorPrefs.SetString(ModelLocation.EditorSelectionKey, Path.GetFullPath(directory));
            }

            if (EditorApplication.isPlaying)
            {
                Reload();
            }
        }

        private static void Reload()
        {
            var renderer = FindFirstObjectByType<ModelRenderer>();
            if (renderer == null)
            {
                return;
            }

            string directory = ModelLocation.Resolve();
            try
            {
                renderer.Load(directory);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ModelSelector] Failed to load model from {directory}: {e}");
            }
        }

        private void OnGUI()
        {
            string selection = ModelLocation.EditorSelection;
            string current = ModelLocation.Resolve();
            EditorGUILayout.LabelField("読み込むモデル", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(current, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (!string.IsNullOrEmpty(selection) && !Directory.Exists(selection))
            {
                EditorGUILayout.HelpBox($"選んだフォルダが見つからないので、固定テストモデルを読み込みます：{selection}", MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("候補", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach ((string name, string directory) in ModelLocation.Candidates(Application.dataPath))
            {
                bool selected = string.Equals(Path.GetFullPath(directory), current, StringComparison.Ordinal);
                if (GUILayout.Toggle(selected, name, EditorStyles.radioButton) && !selected)
                {
                    Select(name == "test-face" ? null : directory);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("フォルダを選ぶ…"))
                {
                    string folder = EditorUtility.OpenFolderPanel("モデルのフォルダ（model.json があるフォルダ）", current, string.Empty);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        Select(folder);
                    }
                }

                if (GUILayout.Button("一覧を更新"))
                {
                    Repaint();
                }
            }

            EditorGUILayout.HelpBox("候補は format/samples/test-face と artifacts/keyforms/<イラスト名>/model。再生中に選ぶと、その場で読み込み直す。起動引数 -ugokue-model があれば、そちらが優先される。", MessageType.None);
        }
    }
}
