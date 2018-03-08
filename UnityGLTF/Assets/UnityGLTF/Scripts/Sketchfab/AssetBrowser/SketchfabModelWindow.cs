﻿/*
 * Copyright(c) 2017-2018 Sketchfab Inc.
 * License: https://github.com/sketchfab/UnityGLTF/blob/master/LICENSE
 */

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using SimpleJSON;

namespace Sketchfab
{
    public class SketchfabModelWindow : EditorWindow
    {

        static void Init()
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
            SketchfabModelWindow window = (SketchfabModelWindow)EditorWindow.GetWindow(typeof(SketchfabModelWindow));
            window.titleContent.text = "Import";
            window.Show();
#else // and error dialog if not standalone
		EditorUtility.DisplayDialog("Error", "Your build target must be set to standalone", "Okay");
#endif
        }

        SketchfabModel _currentModel;
        SketchfabUI _ui;
        SketchfabBrowser _window;

        string _prefabName;
        string _importDirectory;
        bool _addToCurrentScene;
        SketchfabRequest _modelRequest;

        bool show = false;
        int _size = -1;
        string _archiveUrl = "";
        byte[] _lastArchive;
        bool _isFeching = false;

        public void displayModelPage(SketchfabModel model, SketchfabBrowser browser)
        {
            _window = browser;
            if(_currentModel == null || model.uid != _currentModel.uid)
            {
                _currentModel = model;
                fetchGLTFModel(_currentModel.uid, OnArchiveUpdate, _window._logger.getHeader());
            }
            else
            {
                _currentModel = model;
            }

            _prefabName = _currentModel.name.Replace(":", "_");
            _importDirectory = Application.dataPath + "/Import/" + _prefabName.Replace(" ", "_");
            _ui = SketchfabPlugin.getUI();
            show = true;
        }


        public Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; ++i)
            {
                pix[i] = col;
            }
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void OnGUI()
        {
            if (_currentModel != null && show)
            {
                SketchfabModel model = _currentModel;
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                _ui.displayModelName(model.name);
                _ui.displayContent("by " + model.author);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("View on Sketchfab"))
                {
                    Application.OpenURL(SketchfabPlugin.Urls.modelUrl + "/" + _currentModel.uid);
                }

                GUILayout.EndHorizontal();

                GUIStyle blackGround = new GUIStyle(GUI.skin.box);
                blackGround.normal.background = MakeTex(2, 2, new Color(0f, 0f, 0f, 1f));

                GUILayout.BeginHorizontal(blackGround);
                GUILayout.FlexibleSpace();
                GUILayout.Label(model._preview);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();


                displayImportSettings();
                GUILayout.Label("");

                GUILayout.BeginHorizontal();

                GUILayout.BeginVertical(GUILayout.Width(250));
                _ui.displayTitle("MODEL INFORMATION");
                _ui.displayModelStats("Vertex count", " " + Utils.humanifySize(model.vertexCount));
                _ui.displayModelStats("Face count", " " + Utils.humanifySize(model.faceCount));
                if(model.hasAnimation != "")
                    _ui.displayModelStats("Animation", model.hasAnimation);
                //_ui.displayModelStats("Is rigged", model.hasSkin ? "Yes" : "No");
                GUILayout.EndVertical();

                GUILayout.BeginVertical(GUILayout.Width(300));
                _ui.displayTitle("LICENSE");
                if(model.licenseJson != null)
                {
                    _ui.displayContent(model.licenseJson["fullName"]);
                    _ui.displaySubContent(model.licenseJson["requirements"]);
                }
                else
                {
                    _ui.displaySubContent("Fetching license data");
                }
                GUILayout.EndVertical();

                GUILayout.EndHorizontal();
            }
        }

        void displayImportSettings()
        {
            GUILayout.BeginVertical("Box");
            _ui.displayContent("Import into");
            GUILayout.BeginHorizontal();
            GUILayout.Label(GLTFUtils.getPathProjectFromAbsolute(_importDirectory), GUILayout.Height(18));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Change", GUILayout.Width(80), GUILayout.Height(18)))
            {
                string newImportDir = EditorUtility.OpenFolderPanel("Choose import directory", GLTFUtils.getPathAbsoluteFromProject(_importDirectory), GLTFUtils.getPathAbsoluteFromProject(_importDirectory));
                if (GLTFUtils.isFolderInProjectDirectory(newImportDir))
                {
                    _importDirectory = newImportDir;
                }
                else if (newImportDir != "")
                {
                    EditorUtility.DisplayDialog("Error", "Please select a path within your current Unity project (with Assets/)", "Ok");
                }
                else
                {
                    // Path is empty, user canceled. Do nothing
                }
            }
            GUILayout.EndHorizontal();
            _ui.displayContent("Options");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Prefab name");
            _prefabName = GUILayout.TextField(_prefabName, GUILayout.Width(200));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            _addToCurrentScene = GUILayout.Toggle(_addToCurrentScene, "Add to current scene");

            GUILayout.BeginHorizontal();
            Color old = GUI.color;
            GUI.color = SketchfabUI.SKFB_BLUE;
            GUI.contentColor = Color.white;
            string buttonCaption = "";
            if (!_window._logger.isUserLogged())
            {
                buttonCaption = "You need to be logged to download and import asset";
                GUI.enabled = false;
            }
            else if( _isFeching)
            {
                buttonCaption = "Looking for model download url";
                GUI.enabled = false;
            }
            else if (_archiveUrl != "")
            {
                buttonCaption = "Download model (" + Utils.humanifyFileSize(_size) + ")";
            }
            else
            {
                buttonCaption = "Model is not available in glTF";
                GUI.enabled = false;
            }
            if (GUILayout.Button(buttonCaption))
            {
                requestArchive();
            }
            GUI.color = old;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void OnArchiveUpdate()
        {
            EditorUtility.ClearProgressBar();
            string _unzipDirectory = Application.temporaryCachePath + "/unzip";
            _window._browserManager.setImportProgressCallback(UpdateProgress);
            _window._browserManager.setImportFinishCallback(OnFinishImport);
            _window._browserManager.importArchive(_lastArchive, _unzipDirectory, _importDirectory, _prefabName, _addToCurrentScene);
        }

        public void setTotalSize(int size)
        {
            _size = size;
        }

        private void handleDownloadCallback(float current)
        {
            if(EditorUtility.DisplayCancelableProgressBar("Download", "Downloading model archive ", (float)current))
            {
                if(_modelRequest != null)
                {
                    _window._browserManager._api.dropRequest(ref _modelRequest);
                    _modelRequest = null;
                }
                clearProgress();
            }
        }

        private void clearProgress()
        {
            EditorUtility.ClearProgressBar();
        }

        private void OnFinishImport()
        {
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Import successful", "Model \n" + _currentModel.name + " by " + _currentModel.author + " has been successfully imported", "OK");
        }

        public void fetchGLTFModel(string uid, RefreshCallback fetchedCallback, Dictionary<string, string> headers)
        {
            _isFeching = true;
            string url = SketchfabPlugin.Urls.modelEndPoint + "/" + uid + "/download";
            _modelRequest = new SketchfabRequest(url, headers);
            _modelRequest.setCallback(handleDownloadAPIResponse);
            _modelRequest.setFailedCallback(handleFailFetchDownloadURl);
            _window._browserManager._api.registerRequest(_modelRequest);
        }

        void handleArchive(byte[] data)
        {
            _lastArchive = data;
            OnArchiveUpdate();
        }

        void handleFailFetchDownloadURl()
        {
            _isFeching = false;
        }

        void handleDownloadAPIResponse(string response)
        {
            JSONNode responseJson = Utils.JSONParse(response);
            if(responseJson["gltf"] != null)
            {
                _archiveUrl = responseJson["gltf"]["url"];
                _size = responseJson["gltf"]["size"].AsInt;
            }
            _isFeching = false;
        }

        void requestArchive()
        {
            SketchfabRequest request = new SketchfabRequest(_archiveUrl);
            request.setCallback(handleArchive);
            request.setProgressCallback(handleDownloadCallback);
            SketchfabPlugin.getAPI().registerRequest(request);
        }

        public void UpdateProgress(UnityGLTF.GLTFEditorImporter.IMPORT_STEP step, int current, int total)
        {
            string element = "";
            switch (step)
            {
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.BUFFER:
                    element = "Buffer";
                    break;
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.IMAGE:
                    element = "Image";
                    break;
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.TEXTURE:
                    element = "Texture";
                    break;
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.MATERIAL:
                    element = "Material";
                    break;
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.MESH:
                    element = "Mesh";
                    break;
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.NODE:
                    element = "Node";
                    break;
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.ANIMATION:
                    element = "Animation";
                    break;
                case UnityGLTF.GLTFEditorImporter.IMPORT_STEP.SKIN:
                    element = "Skin";
                    break;
            }

            EditorUtility.DisplayProgressBar("Importing glTF", "Importing " + element + " (" + current + " / " + total + ")", (float)current / (float)total);
            this.Repaint();
        }

        private void OnDestroy()
        {
            _window.closeModelPage();
        }
    }
}

#endif