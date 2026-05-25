#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System;

namespace Nox.Editor {
	public class Screenshot : EditorWindow {
		
		public enum ImageFormat {
			PNG,
			JPG,
			TGA
		}
		
		public enum ResolutionPreset {
			[InspectorName("Custom")]
			Free,

			// 16:9 Video
			[InspectorName("16:9 — Video/HD 720p (1280x720)")]
			HD_720p,
			[InspectorName("16:9 — Video/Full HD 1080p (1920x1080)")]
			FHD_1080p,
			[InspectorName("16:9 — Video/Quad HD 1440p (2560x1440)")]
			QHD_1440p,
			[InspectorName("16:9 — Video/4K UHD (3840x2160)")]
			UHD_4K,

			// Cinema
			[InspectorName("Cinema/Flat 1.85:1 (1998x1080)")]
			Cinema_Flat_1_85,
			[InspectorName("Cinema/Scope 2.39:1 (2560x1071)")]
			Cinema_Scope_2_39,

			// Ultrawide
			[InspectorName("Ultrawide/21:9 (2560x1080)")]
			Ultrawide_21_9,
			[InspectorName("Ultrawide/32:9 (3840x1080)")]
			Ultrawide_32_9,

			// Photography
			[InspectorName("Photography/3:2 (1500x1000)")]
			Photo_3_2,
			[InspectorName("Photography/5:4 (1280x1024)")]
			Photo_5_4,

			// Classic 4:3
			[InspectorName("Classic 4:3/XGA (1024x768)")]
			XGA_4_3,
			[InspectorName("Classic 4:3/UXGA (1600x1200)")]
			UXGA_4_3,

			// Square 1:1
			[InspectorName("Square 1:1/1024x1024")]
			Square_1024,
			[InspectorName("Square 1:1/2048x2048")]
			Square_2048,

			// Portrait 4:5
			[InspectorName("Portrait 4:5/Instagram Portrait (1080x1350)")]
			Portrait_4_5,

			// Portrait 9:16
			[InspectorName("Portrait 9:16/Stories · Reels · TikTok (1080x1920)")]
			Portrait_9_16,

			// Portrait 2:3
			[InspectorName("Portrait 2:3/Pinterest (1000x1500)")]
			Portrait_2_3,

			// Web & Social Media
			[InspectorName("Web & Social Media/Open Graph 1.91:1 (1200x630)")]
			OpenGraph,
			[InspectorName("Web & Social Media/Twitter/X Header 3:1 (1500x500)")]
			Twitter_Header,
			[InspectorName("Web & Social Media/Facebook Cover (820x312)")]
			Facebook_Cover,
			[InspectorName("Web & Social Media/LinkedIn Banner (1584x396)")]
			LinkedIn_Banner,
			[InspectorName("Web & Social Media/YouTube Thumbnail (1280x720)")]
			YouTube_Thumbnail,
		}
		
		private Camera _selectedCamera;
		private ImageFormat _imageFormat = ImageFormat.PNG;
		private ResolutionPreset _resolutionPreset = ResolutionPreset.Free;
		private int _resolutionMultiplier = 1;
		private int _customWidth = 1920;
		private int _customHeight = 1080;
		private string _fileName = "screenshot";
		private bool _useTimeStamp = true;
		private bool _showPreview = false;
		private bool _realtimePreview = false;
		private Texture2D _previewTexture = null;
		private Vector2 _scrollPosition = Vector2.zero;
		
		[MenuItem("Nox/Tools/Screenshot Tool")]
		public static void ShowWindow() {
			GetWindow<Screenshot>("Screenshot Tool");
		}
		
		private void OnEnable() {
			RefreshSelectedCamera();
		}

		private void OnInspectorUpdate() {
			if (_showPreview && _realtimePreview) {
				GeneratePreview();
				Repaint();
			}
		}
		
		private void OnGUI() {
			_scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
			
			GUILayout.Label("Screenshot Tool", EditorStyles.boldLabel);
			EditorGUILayout.Space();
			
			// Camera selection
			EditorGUILayout.LabelField("Camera", EditorStyles.boldLabel);
			_selectedCamera = (Camera)EditorGUILayout.ObjectField("Source Camera", _selectedCamera, typeof(Camera), true);
			
			if (GUILayout.Button("Refresh Selected Camera")) {
				RefreshSelectedCamera();
			}
			
			EditorGUILayout.Space();
			
			// Image format
			EditorGUILayout.LabelField("Export Format", EditorStyles.boldLabel);
			_imageFormat = (ImageFormat)EditorGUILayout.EnumPopup("Format", _imageFormat);
			
			// Resolution
			EditorGUILayout.LabelField("Resolution", EditorStyles.boldLabel);
			_resolutionPreset = (ResolutionPreset)EditorGUILayout.EnumPopup("Preset", _resolutionPreset);
			
			if (_resolutionPreset == ResolutionPreset.Free) {
				EditorGUILayout.BeginHorizontal();
				_customWidth = EditorGUILayout.IntField("Width", _customWidth);
				_customHeight = EditorGUILayout.IntField("Height", _customHeight);
				EditorGUILayout.EndHorizontal();
				
				_resolutionMultiplier = EditorGUILayout.IntSlider("Multiplier", _resolutionMultiplier, 1, 4);
			}
			
			Vector2 resolution = GetResolution();
			EditorGUILayout.LabelField($"Final Resolution: {resolution.x} x {resolution.y}");
			
			// Aspect ratio display
			float aspectRatio = resolution.x / resolution.y;
			string ratioText = GetAspectRatioText(aspectRatio);
			EditorGUILayout.LabelField($"Aspect Ratio: {ratioText}");
			
			EditorGUILayout.Space();
			
			// File name
			EditorGUILayout.LabelField("File", EditorStyles.boldLabel);
			_fileName = EditorGUILayout.TextField("File Name", _fileName);
			_useTimeStamp = EditorGUILayout.Toggle("Add Timestamp", _useTimeStamp);
			
			EditorGUILayout.Space();
			
			// Preview options
			EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
			_showPreview = EditorGUILayout.Toggle("Show Preview", _showPreview);
			if (_showPreview) {
				bool newRealtime = EditorGUILayout.Toggle("Realtime", _realtimePreview);
				if (newRealtime != _realtimePreview) {
					_realtimePreview = newRealtime;
					if (_realtimePreview) GeneratePreview();
				}
			}
			
			EditorGUILayout.BeginHorizontal();
			
			// Preview button
			GUI.backgroundColor = Color.cyan;
			if (GUILayout.Button("Generate Preview", GUILayout.Height(30))) {
				GeneratePreview();
			}
			
			// Capture button
			GUI.backgroundColor = Color.green;
			if (GUILayout.Button("Take Screenshot", GUILayout.Height(30))) {
				TakeScreenshot();
			}
			GUI.backgroundColor = Color.white;
			
			EditorGUILayout.EndHorizontal();
			
			EditorGUILayout.Space();
			
			// Preview display
			if (_showPreview && _previewTexture != null) {
				EditorGUILayout.LabelField("Preview:", EditorStyles.boldLabel);
				
				// Scale display size to maintain aspect ratio
				float maxPreviewSize = 300f;
				float previewWidth = _previewTexture.width;
				float previewHeight = _previewTexture.height;
				
				if (previewWidth > maxPreviewSize || previewHeight > maxPreviewSize) {
					float scale = Mathf.Min(maxPreviewSize / previewWidth, maxPreviewSize / previewHeight);
					previewWidth *= scale;
					previewHeight *= scale;
				}
				
				Rect previewRect = GUILayoutUtility.GetRect(previewWidth, previewHeight);
				GUI.DrawTexture(previewRect, _previewTexture, ScaleMode.ScaleToFit);
				
				EditorGUILayout.LabelField($"Preview size: {_previewTexture.width}x{_previewTexture.height}");
				EditorGUILayout.Space();
			}
			// Info
			if (_selectedCamera != null) {
				EditorGUILayout.HelpBox($"Camera: {_selectedCamera.name}", MessageType.Info);
			} else {
				SceneView sv = SceneView.lastActiveSceneView;
				string svName = sv != null ? sv.titleContent.text : "(no Scene view open)";
				EditorGUILayout.HelpBox($"No camera selected — using Scene view: {svName}", MessageType.Info);
			}
			
			EditorGUILayout.EndScrollView();
		}
		
		private void OnDestroy() {
			// Clean up preview texture
			if (_previewTexture != null) {
				DestroyImmediate(_previewTexture);
			}
		}
		
		private void RefreshSelectedCamera() {
			// Only auto-assign if the selected GameObject has a Camera component
			GameObject selectedObject = Selection.activeGameObject;
			if (selectedObject != null) {
				Camera cam = selectedObject.GetComponent<Camera>();
				if (cam != null) {
					_selectedCamera = cam;
					return;
				}
			}
			// No camera on selected object — leave null so the Scene view camera is used
		}
		
		private Vector2 GetResolution() {
			Vector2 baseResolution = GetPresetResolution();
			
			if (_resolutionPreset == ResolutionPreset.Free) {
				baseResolution = new Vector2(_customWidth * _resolutionMultiplier, _customHeight * _resolutionMultiplier);
			}
			
			return baseResolution;
		}
		
		private Vector2 GetPresetResolution() {
			switch (_resolutionPreset) {
				// 16:9
				case ResolutionPreset.HD_720p:           return new Vector2(1280, 720);
				case ResolutionPreset.FHD_1080p:         return new Vector2(1920, 1080);
				case ResolutionPreset.QHD_1440p:         return new Vector2(2560, 1440);
				case ResolutionPreset.UHD_4K:            return new Vector2(3840, 2160);
				// Cinema
				case ResolutionPreset.Cinema_Flat_1_85:  return new Vector2(1998, 1080);
				case ResolutionPreset.Cinema_Scope_2_39: return new Vector2(2560, 1071);
				// Ultrawide
				case ResolutionPreset.Ultrawide_21_9:    return new Vector2(2560, 1080);
				case ResolutionPreset.Ultrawide_32_9:    return new Vector2(3840, 1080);
				// Photography
				case ResolutionPreset.Photo_3_2:         return new Vector2(1500, 1000);
				case ResolutionPreset.Photo_5_4:         return new Vector2(1280, 1024);
				// Classic 4:3
				case ResolutionPreset.XGA_4_3:           return new Vector2(1024, 768);
				case ResolutionPreset.UXGA_4_3:          return new Vector2(1600, 1200);
				// Square
				case ResolutionPreset.Square_1024:       return new Vector2(1024, 1024);
				case ResolutionPreset.Square_2048:       return new Vector2(2048, 2048);
				// Portrait
				case ResolutionPreset.Portrait_4_5:      return new Vector2(1080, 1350);
				case ResolutionPreset.Portrait_9_16:     return new Vector2(1080, 1920);
				case ResolutionPreset.Portrait_2_3:      return new Vector2(1000, 1500);
				// Web & Social Media
				case ResolutionPreset.OpenGraph:         return new Vector2(1200, 630);
				case ResolutionPreset.Twitter_Header:    return new Vector2(1500, 500);
				case ResolutionPreset.Facebook_Cover:    return new Vector2(820, 312);
				case ResolutionPreset.LinkedIn_Banner:   return new Vector2(1584, 396);
				case ResolutionPreset.YouTube_Thumbnail: return new Vector2(1280, 720);
				case ResolutionPreset.Free:
				default:
					return new Vector2(_customWidth, _customHeight);
			}
		}
		
		private string GetAspectRatioText(float aspectRatio) {
			if (Mathf.Approximately(aspectRatio, 16f / 9f))    return "16:9";
			if (Mathf.Approximately(aspectRatio, 4f / 3f))     return "4:3";
			if (Mathf.Approximately(aspectRatio, 1f))          return "1:1";
			if (Mathf.Approximately(aspectRatio, 9f / 16f))    return "9:16";
			if (Mathf.Approximately(aspectRatio, 21f / 9f))    return "21:9";
			if (Mathf.Approximately(aspectRatio, 32f / 9f))    return "32:9";
			if (Mathf.Approximately(aspectRatio, 3f / 2f))     return "3:2";
			if (Mathf.Approximately(aspectRatio, 2f / 3f))     return "2:3";
			if (Mathf.Approximately(aspectRatio, 5f / 4f))     return "5:4";
			if (Mathf.Approximately(aspectRatio, 4f / 5f))     return "4:5";
			if (Mathf.Approximately(aspectRatio, 3f / 1f))     return "3:1";
			if (Mathf.Approximately(aspectRatio, 4f / 1f))     return "4:1";
			if (Mathf.Approximately(aspectRatio, 1998f/1080f)) return "1.85:1";
			if (Mathf.Approximately(aspectRatio, 2560f/1071f)) return "2.39:1";
			if (Mathf.Approximately(aspectRatio, 1200f/630f))  return "1.91:1";
			return $"{aspectRatio:F2}:1";
		}
		
		private void TakeScreenshot() {
			try {
				string finalFileName = GetFinalFileName();
				string projectPath = Application.dataPath;
				string fullPath = Path.Combine(projectPath, finalFileName);
				
				if (_selectedCamera != null) {
					CaptureFromCamera(fullPath);
				} else {
					CaptureFromSceneView(fullPath);
				}
				
				Debug.Log($"Screenshot saved: {fullPath}");
				
				// Fix texture import settings to preserve aspect ratio
				if (fullPath.StartsWith(Application.dataPath)) {
					string relativePath = "Assets" + fullPath.Substring(Application.dataPath.Length).Replace('\\', '/');
					AssetDatabase.Refresh();
					TextureImporter importer = AssetImporter.GetAtPath(relativePath) as TextureImporter;
					if (importer != null) {
						Vector2 resolution = GetResolution();
						int longestSide = Mathf.Max((int)resolution.x, (int)resolution.y);
						int maxSize = 128;
						while (maxSize < longestSide) maxSize *= 2;
						importer.npotScale = TextureImporterNPOTScale.None;
						importer.maxTextureSize = maxSize;
						importer.textureCompression = TextureImporterCompression.Uncompressed;
						AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
					}
				}
				
				EditorUtility.DisplayDialog("Success", $"Screenshot saved:\n{fullPath}", "OK");
			}
			catch (Exception e) {
				Debug.LogError($"Screenshot error: {e.Message}");
				EditorUtility.DisplayDialog("Error", $"Could not take screenshot:\n{e.Message}", "OK");
			}
		}
		
		private void CaptureFromCamera(string path) {
			Vector2 resolution = GetResolution();
			RenderTexture renderTexture = new RenderTexture((int)resolution.x, (int)resolution.y, 24);
			_selectedCamera.targetTexture = renderTexture;
			_selectedCamera.Render();
			
			RenderTexture.active = renderTexture;
			Texture2D screenshot = new Texture2D((int)resolution.x, (int)resolution.y, TextureFormat.RGB24, false);
			screenshot.ReadPixels(new Rect(0, 0, resolution.x, resolution.y), 0, 0);
			screenshot.Apply();
			
			_selectedCamera.targetTexture = null;
			RenderTexture.active = null;
			DestroyImmediate(renderTexture);
			
			SaveTexture(screenshot, path);
			DestroyImmediate(screenshot);
		}
		
		private void CaptureFromSceneView(string path) {
			SceneView sceneView = SceneView.lastActiveSceneView;
			if (sceneView == null) {
				// Fall back to any open SceneView
				SceneView[] sceneViews = Resources.FindObjectsOfTypeAll<SceneView>();
				if (sceneViews.Length > 0) {
					sceneView = sceneViews[0];
				} else {
					throw new System.Exception("No Scene view found");
				}
			}
			
			Vector2 resolution = GetResolution();
			Camera sceneCamera = sceneView.camera;
			
			// Save current settings
			RenderTexture originalTargetTexture = sceneCamera.targetTexture;
			
			// Create a RenderTexture at the desired resolution
			RenderTexture renderTexture = new RenderTexture((int)resolution.x, (int)resolution.y, 24);
			sceneCamera.targetTexture = renderTexture;
			
			// Force render
			sceneView.camera.Render();
			
			// Read pixels
			RenderTexture.active = renderTexture;
			Texture2D screenshot = new Texture2D((int)resolution.x, (int)resolution.y, TextureFormat.RGB24, false);
			screenshot.ReadPixels(new Rect(0, 0, resolution.x, resolution.y), 0, 0);
			screenshot.Apply();
			
			// Restore settings
			sceneCamera.targetTexture = originalTargetTexture;
			RenderTexture.active = null;
			DestroyImmediate(renderTexture);
			
			SaveTexture(screenshot, path);
			DestroyImmediate(screenshot);
		}
		
		private void SaveTexture(Texture2D texture, string path) {
			byte[] data;
			
			switch (_imageFormat) {
				case ImageFormat.PNG:
					data = texture.EncodeToPNG();
					break;
				case ImageFormat.JPG:
					data = texture.EncodeToJPG();
					break;
				case ImageFormat.TGA:
					data = texture.EncodeToTGA();
					break;
				default:
					data = texture.EncodeToPNG();
					break;
			}
			
			File.WriteAllBytes(path, data);
		}
		
		private string GetFinalFileName() {
			string filename = _fileName;
			if (string.IsNullOrEmpty(filename)) {
				filename = "screenshot";
			}
			
			if (_useTimeStamp) {
				string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
				filename += "_" + timestamp;
			}
			
			string extension = GetExtension();
			return filename + extension;
		}
		
		private string GetExtension() {
			switch (_imageFormat) {
				case ImageFormat.PNG:
					return ".png";
				case ImageFormat.JPG:
					return ".jpg";
				case ImageFormat.TGA:
					return ".tga";
				default:
					return ".png";
			}
		}
		
		private void GeneratePreview() {
			try {
				// Clean up previous preview texture
				if (_previewTexture != null) {
					DestroyImmediate(_previewTexture);
					_previewTexture = null;
				}
				
				if (_selectedCamera != null) {
					_previewTexture = CaptureFromCameraToTexture();
				} else {
					_previewTexture = CaptureFromSceneViewToTexture();
				}
				
				_showPreview = true;
				Repaint();
			}
			catch (Exception e) {
				Debug.LogError($"Preview error: {e.Message}");
				if (!_realtimePreview)
					EditorUtility.DisplayDialog("Error", $"Could not generate preview:\n{e.Message}", "OK");
			}
		}
		
		private Texture2D CaptureFromCameraToTexture() {
			Vector2 resolution = GetResolution();
			
			// Cap preview resolution for performance
			int previewWidth = Mathf.Min((int)resolution.x, 512);
			int previewHeight = Mathf.Min((int)resolution.y, 512);
			
			// Maintain aspect ratio
			float aspectRatio = resolution.x / resolution.y;
			if (previewWidth / (float)previewHeight > aspectRatio) {
				previewWidth = (int)(previewHeight * aspectRatio);
			} else {
				previewHeight = (int)(previewWidth / aspectRatio);
			}
			
			RenderTexture renderTexture = new RenderTexture(previewWidth, previewHeight, 24);
			RenderTexture previousTarget = _selectedCamera.targetTexture;
			
			_selectedCamera.targetTexture = renderTexture;
			_selectedCamera.Render();
			
			RenderTexture.active = renderTexture;
			Texture2D previewTexture = new Texture2D(previewWidth, previewHeight, TextureFormat.RGB24, false);
			previewTexture.ReadPixels(new Rect(0, 0, previewWidth, previewHeight), 0, 0);
			previewTexture.Apply();
			
			_selectedCamera.targetTexture = previousTarget;
			RenderTexture.active = null;
			DestroyImmediate(renderTexture);
			
			return previewTexture;
		}
		
		private Texture2D CaptureFromSceneViewToTexture() {
			SceneView sceneView = SceneView.lastActiveSceneView;
			if (sceneView == null) {
				SceneView[] sceneViews = Resources.FindObjectsOfTypeAll<SceneView>();
				if (sceneViews.Length > 0) sceneView = sceneViews[0];
			}
			if (sceneView == null) return CreatePlaceholderTexture();
			
			Vector2 resolution = GetResolution();
			int previewWidth = Mathf.Min((int)resolution.x, 512);
			int previewHeight = Mathf.Min((int)resolution.y, 512);
			float aspectRatio = resolution.x / resolution.y;
			if (previewWidth / (float)previewHeight > aspectRatio)
				previewWidth = Mathf.Max(1, (int)(previewHeight * aspectRatio));
			else
				previewHeight = Mathf.Max(1, (int)(previewWidth / aspectRatio));
			
			Camera sceneCamera = sceneView.camera;
			RenderTexture originalTarget = sceneCamera.targetTexture;
			RenderTexture rt = new RenderTexture(previewWidth, previewHeight, 24);
			sceneCamera.targetTexture = rt;
			sceneCamera.Render();
			RenderTexture.active = rt;
			Texture2D tex = new Texture2D(previewWidth, previewHeight, TextureFormat.RGB24, false);
			tex.ReadPixels(new Rect(0, 0, previewWidth, previewHeight), 0, 0);
			tex.Apply();
			sceneCamera.targetTexture = originalTarget;
			RenderTexture.active = null;
			DestroyImmediate(rt);
			return tex;
		}
		
		private Texture2D CreatePlaceholderTexture() {
			Vector2 resolution = GetResolution();
			float aspectRatio = resolution.x / resolution.y;
			
			int width = 256;
			int height = (int)(width / aspectRatio);
			
			if (height <= 0) height = 256; // Safety fallback to avoid zero height
			
			Texture2D placeholder = new Texture2D(width, height, TextureFormat.RGB24, false);
			Color[] colors = new Color[width * height];
			
			// Simple checkerboard pattern
			for (int y = 0; y < height; y++) {
				for (int x = 0; x < width; x++) {
					bool checker = ((x / 16) + (y / 16)) % 2 == 0;
					colors[y * width + x] = checker ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.9f, 0.9f, 0.9f);
				}
			}
			
			placeholder.SetPixels(colors);
			placeholder.Apply();
			
			return placeholder;
		}
	}
}
#endif // UNITY_EDITOR