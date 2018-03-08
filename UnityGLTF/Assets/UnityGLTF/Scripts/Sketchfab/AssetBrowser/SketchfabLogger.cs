﻿#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;
using SimpleJSON;

namespace Sketchfab
{
    class SketchfabProfile
    {
        public string displayName;
        public string accountLabel;
        public int maxUploadSize;
        public Texture2D avatar = SketchfabPlugin.DEFAULT_AVATAR;
        public bool hasAvatar = false;
        public string avatarUrl;
        int _canPrivate = -1; // Can protect model = 1  // Cannot = 2

        public SketchfabProfile(string usr, string planLb)
        {
            displayName = usr;
            switch (planLb)
            {
                case "pro":
                    maxUploadSize = 200 * 1024 * 1024;
                    accountLabel = "PRO";
                    break;
                case "prem":
                    maxUploadSize = 500 * 1024 * 1024;
                    accountLabel = "PREMIUM";
                    break;
                case "biz":
                    maxUploadSize = 500 * 1024 * 1024;
                    accountLabel = "BUSINESS";
                    break;
                case "ent":
                    maxUploadSize = 500 * 1024 * 1024;
                    accountLabel = "ENTERPRISE";
                    break;
                default:
                    maxUploadSize = 50 * 1024 * 1024;
                    accountLabel = "BASIC";
                    break;
            }
        }

        public void setAvatar(Texture2D img)
        {
            avatar = img;
            hasAvatar = true;
        }

        public bool isDisplayable()
        {
            return displayName != null;
        }
    }

    public class SketchfabLogger
    {
        private string accessTokenKey = "skfb_access_token";
        SketchfabProfile _current;
        RefreshCallback _refresh;
        public Vector2 UI_SIZE = new Vector2(200, 30);
        public Vector2 AVATAR_SIZE = new Vector2(50, 50);

        string username;
        string password = "";
        bool _isUserLogged = false;
        bool _hasCheckedSession = false;

        public enum LOGIN_STEP
        {
            GET_TOKEN,
            CHECK_TOKEN,
            USER_INFO
        }

        public SketchfabLogger(RefreshCallback callback=null)
        {
            _refresh = callback;
            checkAccessTokenValidity();
            if(username == null)
            {
                username = EditorPrefs.GetString("skfb_username", "");
            }
        }

        public bool isUserLogged()
        {
            return _isUserLogged;
        }

        public void showLoginUi()
        {
            GUILayout.BeginVertical(GUILayout.MinWidth(UI_SIZE.x), GUILayout.MinHeight(UI_SIZE.y));
            if (_current == null)
            {
                if(_hasCheckedSession)
                {
                    GUILayout.Label("You're not logged", EditorStyles.centeredGreyMiniLabel);
                    GUILayout.BeginHorizontal();
                    GUILayout.BeginVertical();
                    username = GUILayout.TextField(username);
                    password = GUILayout.PasswordField(password, '*');

                    GUI.enabled = username != null && password != null && username.Length > 0 && password.Length > 0;
                    if (GUILayout.Button("Login"))
                    {
                        requestAccessToken(username, password);
                    }
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                    GUI.enabled = true;
                }
                else
                {
                    GUILayout.Label("Retrieving user data", EditorStyles.centeredGreyMiniLabel);
                }

            }
            else if (_current.isDisplayable())
            {
                GUILayout.Label("Logged as", EditorStyles.centeredGreyMiniLabel);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.BeginVertical();
                GUILayout.Label("User: " + _current.displayName);
                GUILayout.Label("Plan: " + _current.accountLabel);
                if (GUILayout.Button("Logout"))
                {
                    logout();
                    return;
                }
                GUILayout.EndVertical();

                GUILayout.Label(_current.avatar);

                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        public void logout()
        {
            EditorPrefs.DeleteKey(accessTokenKey);
            _current = null;
            _isUserLogged = false;
			_hasCheckedSession = true;
		}

        public void requestAccessToken(string user_name, string user_password)
        {
            List<IMultipartFormSection> formData = new List<IMultipartFormSection>();
            formData.Add(new MultipartFormDataSection("username", user_name));
            formData.Add(new MultipartFormDataSection("password", user_password));

            SketchfabRequest tokenRequest = new SketchfabRequest(SketchfabPlugin.Urls.oauth, formData);
            tokenRequest.setCallback(handleGetToken);
			tokenRequest.setFailedCallback(logout);
            SketchfabPlugin.getAPI().registerRequest(tokenRequest);
        }

        private void handleGetToken(string response)
        {
            string access_token = parseAccessToken(response);
            EditorPrefs.SetString("skfb_username", username);
            if (access_token != null)
                registerAccessToken(access_token);

            if (_current == null)
            {
                requestUserData();
            }
           // _refresh();
        }

        private string parseAccessToken(string text)
        {
            JSONNode response = Utils.JSONParse(text);
            if (response["access_token"] != null)
            {
                return response["access_token"];
            }

            return null;
        }

        private void registerAccessToken(string access_token)
        {
            EditorPrefs.SetString(accessTokenKey, access_token);
        }

        public void requestAvatar(string url)
        {
            string access_token = EditorPrefs.GetString(accessTokenKey);
            if (access_token == null || access_token.Length < 30)
            {
                Debug.Log("Access token is invalid or inexistant");
                return;
            }

            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers.Add("Authorization", "Bearer " + access_token);
            SketchfabRequest request = new SketchfabRequest(url, headers);
            request.setCallback(handleAvatar);
            SketchfabPlugin.getAPI().registerRequest(request);
        }

        public Dictionary<string, string> getHeader()
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers.Add("Authorization", "Bearer " + EditorPrefs.GetString(accessTokenKey));
            return headers;
        }

        private string getAvatarUrl(JSONNode node)
        {
            JSONArray array = node["avatar"]["images"].AsArray;
            foreach (JSONNode elt in array)
            {
                if (elt["width"].AsInt == 100)
                {
                    return elt["url"];
                }
            }

            return "";
        }

        // Callback for avatar
        private void handleAvatar(byte[] responseData)
        {
            if (_current == null)
            {
                Debug.Log("Invalid call avatar");
                return;
            }

            Texture2D tex = new Texture2D(4, 4);
            tex.LoadImage(responseData);
            TextureScale.Bilinear(tex, (int)AVATAR_SIZE.x, (int)AVATAR_SIZE.y);
            _current.setAvatar(tex);
            if(_refresh != null)
                _refresh();
        }

        public void requestUserData()
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers.Add("Authorization", "Bearer " + EditorPrefs.GetString(accessTokenKey));
            SketchfabRequest request = new SketchfabRequest(SketchfabPlugin.Urls.userMe, headers);
            request.setCallback(handleUserData);
			request.setFailedCallback(logout);
            SketchfabPlugin.getAPI().registerRequest(request);
        }

        public void checkAccessTokenValidity()
        {
            string access_token = EditorPrefs.GetString(accessTokenKey);
            if(access_token == null || access_token.Length < 30)
            {
                Debug.Log("Access token is invalid or inexistant");
                _hasCheckedSession = true;
                return;
            }
            requestUserData();
        }

        private void handleUserData(string response)
        {
            JSONNode userData = Utils.JSONParse(response);
            _current = new SketchfabProfile(userData["displayName"], userData["account"]);
            requestAvatar(getAvatarUrl(userData));
            _isUserLogged = true;
            _hasCheckedSession = true;
        }

        // NOTES
        private void onVersionCheck(string response)
        {
            // Dummy vars
            string _latestVersion = "";
            JSONNode versionResponse = Utils.JSONParse(response);
            if (versionResponse != null && versionResponse[0]["tag_name"] != null)
            {
                _latestVersion = versionResponse[0]["tag_name"];
                //_checkVersionSuccess();
            }
            else
            {
                _latestVersion = "";
                //_checkVersionFailed();
            }
        }

        private void handleCanPrivate()
        {

        }

        public bool canPrivate()
        {
            return true;
        }

        public bool checkUserPlanFileSizeLimit(long size)
        {
            if (_current == null)
                return false;
            if (_current.maxUploadSize > size)
                return true;

            return false;
        }

        public bool isUserBasic()
        {
            if (_current != null)
                return _current.accountLabel == "BASIC";
            else
                return true;
        }

        private void onCanPrivate(string response)
        {
            //JSONNode versionResponse = Utils.JSONParse(response);
            //_userCanPrivate = jsonResponse["canProtectModels"].AsBool;
        }
    }
}
#endif