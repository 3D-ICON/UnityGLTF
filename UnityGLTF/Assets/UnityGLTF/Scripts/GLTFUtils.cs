﻿using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using Ionic.Zip;
using System;

public class GLTFUtils
{
	public enum WorkflowMode
	{
		Specular,
		Metallic,
		Dielectric
	}

	public enum BlendMode
	{
		Opaque,
		Cutout,
		Fade,   // Old school alpha-blending mode, fresnel does not affect amount of transparency
		Transparent // Physically plausible transparency mode, implemented as alpha pre-multiply
	}

	public enum SmoothnessMapChannel
	{
		SpecularMetallicAlpha,
		AlbedoAlpha,
	}

	public static Transform[] getSceneTransforms()
	{
		var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
		var gameObjects = scene.GetRootGameObjects();
		return Array.ConvertAll(gameObjects, gameObject => gameObject.transform);
	}

	public static Transform[] getSelectedTransforms()
	{
		if (Selection.transforms.Length <= 0)
			throw new Exception("No objects selected, cannot export.");

		return Selection.transforms;
	}

	public static string unifyPathSeparator(string path)
	{
		return path.Replace("\\\\", "/").Replace("\\", "/");
	}
	public static string getPathProjectFromAbsolute(string absolutePath)
	{
		return unifyPathSeparator(absolutePath.Replace(Application.dataPath, "Assets"));
	}

	public static string getPathAbsoluteFromProject(string projectPath)
	{
		return unifyPathSeparator(projectPath.Replace("Assets/", Application.dataPath));
	}

	public static Regex rgx = new Regex("[^a-zA-Z0-9 -_.]");

	static public string cleanNonAlphanumeric(string s)
	{
		return rgx.Replace(s, "");
	}

	static public bool isValidMeshObject(GameObject gameObject)
	{
		return gameObject.GetComponent<MeshFilter>() != null && gameObject.GetComponent<MeshFilter>().sharedMesh != null ||
			   gameObject.GetComponent<SkinnedMeshRenderer>() != null && gameObject.GetComponent<SkinnedMeshRenderer>().sharedMesh != null;
	}

	public static void removeEmptyDirectory(string directoryPath)
	{
		if (!Directory.Exists(directoryPath))
			return;

		DirectoryInfo info = new DirectoryInfo(directoryPath);
		if (info.GetFiles().Length == 0)
			Directory.Delete(directoryPath, true);
	}

	public static void removeFileList(string[] fileList)
	{
		foreach(string file in fileList)
		{
			if (File.Exists(file))
				File.Delete(file);
		}
	}

	public static Matrix4x4 convertMatrixLeftToRightHandedness(Matrix4x4 mat)
	{
		Vector3 position = mat.GetColumn(3);
		convertVector3LeftToRightHandedness(ref position);
		Quaternion rotation = Quaternion.LookRotation(mat.GetColumn(2), mat.GetColumn(1));
		convertQuatLeftToRightHandedness(ref rotation);

		Vector3 scale = new Vector3(mat.GetColumn(0).magnitude, mat.GetColumn(1).magnitude, mat.GetColumn(2).magnitude);
		float epsilon = 0.00001f;

		// Some issues can occurs with non uniform scales
		if (Mathf.Abs(scale.x - scale.y) > epsilon || Mathf.Abs(scale.y - scale.z) > epsilon || Mathf.Abs(scale.x - scale.z) > epsilon)
		{
			Debug.LogWarning("A matrix with non uniform scale is being converted from left to right handed system. This code is not working correctly in this case");
		}

		// Handle negative scale component in matrix decomposition
		if (Matrix4x4.Determinant(mat) < 0)
		{
			Quaternion rot = Quaternion.LookRotation(mat.GetColumn(2), mat.GetColumn(1));
			Matrix4x4 corr = Matrix4x4.TRS(mat.GetColumn(3), rot, Vector3.one).inverse;
			Matrix4x4 extractedScale = corr * mat;
			scale = new Vector3(extractedScale.m00, extractedScale.m11, extractedScale.m22);
		}

		// convert transform values from left handed to right handed
		mat.SetTRS(position, rotation, scale);
		Debug.Log("INVERSIOON");
		return mat;
	}

	public static void convertVector3LeftToRightHandedness(ref Vector3 vect)
	{
		vect.z = -vect.z;
	}

	public static void convertVector4LeftToRightHandedness(ref Vector4 vect)
	{
		vect.z = -vect.z;
		vect.w = -vect.w;
	}

	public static void convertQuatLeftToRightHandedness(ref Quaternion quat)
	{
		quat.w = -quat.w;
		quat.z = -quat.z;
	}

	/// Specifies the path and filename for the GLTF Json and binary
	/// </summary>
	/// <param name="filesToZip">Dictionnary where keys are original absolute file paths, and value is directory in zip</param>
	/// <param name="zipPath">Path of the output zip archive</param>
	/// <param name="deleteOriginals">Remove original files after building the zip</param>
	public static void buildZip(Dictionary<string, string> filesToZip, string zipPath, bool deleteOriginals)
	{
		if(filesToZip.Count == 0)
		{
			Debug.LogError("GLTFUtils: no files to zip");
		}

		ZipFile zip = new ZipFile();
		Debug.Log(filesToZip.Count + " files to zip");

		foreach (string originFilePath in filesToZip.Keys)
		{
			if(!File.Exists(originFilePath))
			{
				Debug.LogError("GLTFUtils.buildZip: File " + originFilePath +" not found.");
			}

			zip.AddFile(originFilePath, filesToZip[originFilePath]);
		}
		try
		{
			zip.Save(zipPath);
		}
		catch(IOException e)
		{
			Debug.LogError("Failed to save zip file." + e);
		}

		// Remove all files
		if(deleteOriginals)
		{
			foreach (string pa in filesToZip.Keys)
			{
				if (System.IO.File.Exists(pa))
					System.IO.File.Delete(pa);
			}

			Debug.Log("Files have been cleaned");
		}
	}

	public static string buildImageName(Texture2D image)
	{
		string imageName = image.GetInstanceID().ToString().Replace("-", "") + "_" + image.name + ".png";
		return imageName;
	}

	public static bool getPixelsFromTexture(ref Texture2D texture, out Color[] pixels)
	{
		//Make texture readable
		TextureImporter im = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
		if (!im)
		{
			pixels = new Color[1];
			return false;
		}

		bool readable = im.isReadable;
		TextureImporterCompression format = im.textureCompression;
		TextureImporterType type = im.textureType;
		bool isConvertedBump = im.convertToNormalmap;
		bool srgb = im.sRGBTexture;
		im.sRGBTexture = false;

		if (!readable)
			im.isReadable = true;
		if (type != TextureImporterType.Default)
			im.textureType = TextureImporterType.Default;

		im.textureCompression = TextureImporterCompression.Uncompressed;
		im.SaveAndReimport();

		pixels = texture.GetPixels();

		if (!readable)
			im.isReadable = false;
		if (type != TextureImporterType.Default)
			im.textureType = type;

		if (isConvertedBump)
			im.convertToNormalmap = true;

		im.sRGBTexture = srgb;
		im.textureCompression = format;
		im.SaveAndReimport();

		return true;
	}
	public static void SetupMaterialWithBlendMode(Material material, BlendMode blendMode)
	{
		switch (blendMode)
		{
			case BlendMode.Opaque:
				material.SetOverrideTag("RenderType", "");
				material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
				material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
				material.SetInt("_ZWrite", 1);
				material.DisableKeyword("_ALPHATEST_ON");
				material.DisableKeyword("_ALPHABLEND_ON");
				material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
				material.renderQueue = -1;
				break;
			case BlendMode.Cutout:
				material.SetOverrideTag("RenderType", "TransparentCutout");
				material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
				material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
				material.SetInt("_ZWrite", 1);
				material.EnableKeyword("_ALPHATEST_ON");
				material.DisableKeyword("_ALPHABLEND_ON");
				material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
				material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
				break;
			case BlendMode.Fade:
				material.SetOverrideTag("RenderType", "Transparent");
				material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
				material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
				material.SetInt("_ZWrite", 0);
				material.DisableKeyword("_ALPHATEST_ON");
				material.EnableKeyword("_ALPHABLEND_ON");
				material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
				material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
				break;
			case BlendMode.Transparent:
				material.SetOverrideTag("RenderType", "Transparent");
				material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
				material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
				material.SetInt("_ZWrite", 0);
				material.DisableKeyword("_ALPHATEST_ON");
				material.DisableKeyword("_ALPHABLEND_ON");
				material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
				material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
				break;
		}
	}

	public static SmoothnessMapChannel GetSmoothnessMapChannel(Material material)
	{
		int ch = (int)material.GetFloat("_SmoothnessTextureChannel");
		if (ch == (int)SmoothnessMapChannel.AlbedoAlpha)
			return SmoothnessMapChannel.AlbedoAlpha;
		else
			return SmoothnessMapChannel.SpecularMetallicAlpha;
	}

		public static void SetMaterialKeywords(Material material, WorkflowMode workflowMode)
		{
			// Note: keywords must be based on Material value not on MaterialProperty due to multi-edit & material animation
			// (MaterialProperty value might come from renderer material property block)
			SetKeyword(material, "_NORMALMAP", material.GetTexture("_BumpMap") || material.GetTexture("_DetailNormalMap"));
			if (workflowMode == WorkflowMode.Specular)
				SetKeyword(material, "_SPECGLOSSMAP", material.GetTexture("_SpecGlossMap"));
			else if (workflowMode == WorkflowMode.Metallic)
				SetKeyword(material, "_METALLICGLOSSMAP", material.GetTexture("_MetallicGlossMap"));
			SetKeyword(material, "_PARALLAXMAP", material.GetTexture("_ParallaxMap"));
			SetKeyword(material, "_DETAIL_MULX2", material.GetTexture("_DetailAlbedoMap") || material.GetTexture("_DetailNormalMap"));

			// A material's GI flag internally keeps track of whether emission is enabled at all, it's enabled but has no effect
			// or is enabled and may be modified at runtime. This state depends on the values of the current flag and emissive color.
			// The fixup routine makes sure that the material is in the correct state if/when changes are made to the mode or color.
			MaterialEditor.FixupEmissiveFlag(material);
			//bool shouldEmissionBeEnabled = (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0;
			SetKeyword(material, "_EMISSION", material.GetTexture("_EmissionMap"));

			if (material.HasProperty("_SmoothnessTextureChannel"))
			{
				SetKeyword(material, "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", GetSmoothnessMapChannel(material) == SmoothnessMapChannel.AlbedoAlpha);
			}
		}

		public static void MaterialChanged(Material material, WorkflowMode workflowMode)
		{
			SetupMaterialWithBlendMode(material, (BlendMode)material.GetFloat("_Mode"));

			SetMaterialKeywords(material, workflowMode);
		}

		public static void SetKeyword(Material m, string keyword, bool state)
		{
			if (state)
				m.EnableKeyword(keyword);
			else
				m.DisableKeyword(keyword);
		}
		public static int MAX_VERTICES = 65535;

		public static Vector3[][] splitArray(Vector3[] input, int nbElements)
		{
			int nbOutputs = input.Length / nbElements + 1;
			Vector3[][] output = new Vector3[nbOutputs][];
			for(int i =0; i < nbOutputs; ++i)
			{
				int nbElts = (int)Mathf.Min(nbElements, Mathf.Abs(i * nbElements - input.Length));
				output[i] = new Vector3[nbElts];
				Debug.Log(nbElts + " for " + i * nbElements + " to " + input.Length);
				Array.Copy(input, i * nbElements, output[i], 0, nbElts - 1);
			}

			return output;
		}

	public static Vector2[][] splitArray(Vector2[] input, int nbElements)
	{
		int nbOutputs = input.Length / nbElements + 1;
		Vector2[][] output = new Vector2[nbOutputs][];
		for (int i = 0; i < nbOutputs; ++i)
		{
			output[i] = new Vector2[MAX_VERTICES];
			Array.Copy(input, i * MAX_VERTICES, output[i], 0, Mathf.Min(MAX_VERTICES, Mathf.Abs(i * MAX_VERTICES - input.Length)));
		}

		return output;
	}

	public static Vector4[][] splitArray(Vector4[] input, int nbElements)
	{
		int nbOutputs = input.Length / nbElements + 1;
		Vector4[][] output = new Vector4[nbOutputs][];
		for (int i = 0; i < nbOutputs; ++i)
		{
			output[i] = new Vector4[MAX_VERTICES];
			Array.Copy(input, i * MAX_VERTICES, output[i], 0, Mathf.Min(MAX_VERTICES, Mathf.Abs(i * MAX_VERTICES - input.Length)));
		}

		return output;
	}

	public static Color[][] splitArray(Color[] input, int nbElements)
	{
		int nbOutputs = input.Length / nbElements + 1;
		Color[][] output = new Color[nbOutputs][];
		for (int i = 0; i < nbOutputs; ++i)
		{
			output[i] = new Color[MAX_VERTICES];
			Array.Copy(input, i * MAX_VERTICES, output[i], 0, Mathf.Min(MAX_VERTICES, Mathf.Abs(i * MAX_VERTICES - input.Length)));
		}

		return output;
	}

	public static void splitTriangles(int[] triangles, int threshold)
	{
		int nbOutputs = (Mathf.Max(triangles) / threshold) + 1;
		int[][] output = new int[nbOutputs][];
		int nbAdded = 0;
		for(int i=0; i < nbOutputs; ++i)
		{
			int lowerBound = i - 1 * threshold;
			int upperBound = i * threshold;
			List<int> newTriList = new List<int>(splitTriangles(triangles, lowerBound, upperBound));
			output[i] = newTriList.ToArray();
			nbAdded += newTriList.Count;
		}
		Debug.Log("NB added " + nbAdded + "  vs total " + triangles.Length);
	}

	public static int[] splitTriangles(int[] triangles, int lowThreshold, int highThreshold)
	{
		List<int> output = new List<int>();
		for(int i = 0; i < triangles.Length - 2; i += 3)
		{
			if(isInInterval(lowThreshold, highThreshold, triangles[i]) 
				&& isInInterval(lowThreshold, highThreshold, triangles[i +1]) 
				&& isInInterval(lowThreshold, highThreshold, triangles[i +2]))
			{
				output.Add(triangles[i] - i * lowThreshold);
				output.Add(triangles[i+1] - i * lowThreshold);
				output.Add(triangles[i+2] - i * lowThreshold);
			}
		}

		return output.ToArray(); ;
	}

	public static bool isInInterval(int lower, int upper, int value)
	{
		return value < upper && value >= lower;
	}
}
