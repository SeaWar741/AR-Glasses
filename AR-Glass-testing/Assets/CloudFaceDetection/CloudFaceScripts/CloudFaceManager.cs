using UnityEngine;
using System.Collections;
using System.Collections.Generic;
//using Newtonsoft.Json.Serialization;
//using Newtonsoft.Json;
using System.Text;
using System;


public class CloudFaceManager : MonoBehaviour 
{
	[Tooltip("Service location for Face API.")]
	public string faceServiceLocation = "westus";

	[Tooltip("Subscription key for Face API.")]
	public string faceSubscriptionKey;

//	[Tooltip("Whether to recognize the emotions of the detected faces, or not.")]
//	public bool recognizeEmotions = false;

	[Tooltip("Service location for Emotion API.")]
	public string emotionServiceLocation = "westus";

	[Tooltip("Subscription key for Emotion API.")]
	public string emotionSubscriptionKey;

	[HideInInspector]
	public Face[] faces;  // the detected faces


	//private const string ServiceHost = "https://api.projectoxford.ai/face/v1.0";
	private const string FaceServiceHost = "https://[location].api.cognitive.microsoft.com/face/v1.0";
	private const string EmotionServiceHost = "https://[location].api.cognitive.microsoft.com/emotion/v1.0";

	private static CloudFaceManager instance = null;
	private bool isInitialized = false;


	void Start () 
	{
		instance = this;

		if(string.IsNullOrEmpty(faceSubscriptionKey))
		{
			throw new Exception("Please set your face-subscription key.");
		}

		isInitialized = true;
	}

	/// <summary>
	/// Gets the FaceManager instance.
	/// </summary>
	/// <value>The FaceManager instance.</value>
	public static CloudFaceManager Instance
	{
		get
		{
			return instance;
		}
	}


	/// <summary>
	/// Determines whether the FaceManager is initialized.
	/// </summary>
	/// <returns><c>true</c> if the FaceManager is initialized; otherwise, <c>false</c>.</returns>
	public bool IsInitialized()
	{
		return isInitialized;
	}


	/// <summary>
	/// Detects the faces in the given image.
	/// </summary>
	/// <returns>List of detected faces.</returns>
	/// <param name="texImage">Image texture.</param>
	public IEnumerator DetectFaces(Texture2D texImage)
	{
		if (texImage != null) 
		{
			byte[] imageBytes = texImage.EncodeToJPG ();
			yield return DetectFaces (imageBytes);
		} 
		else 
		{
			yield return null;
		}
	}
	
	
	/// <summary>
	/// Detects the faces in the given image.
	/// </summary>
	/// <returns>List of detected faces.</returns>
	/// <param name="imageBytes">Image bytes.</param>
	public IEnumerator DetectFaces(byte[] imageBytes)
	{
		faces = null;

		if(string.IsNullOrEmpty(faceSubscriptionKey))
		{
			throw new Exception("The face-subscription key is not set.");
		}

		// detect faces
		string faceServiceHost = FaceServiceHost.Replace("[location]", faceServiceLocation);
		string requestUrl = string.Format("{0}/detect?returnFaceId={1}&returnFaceLandmarks={2}&returnFaceAttributes={3}", 
			faceServiceHost, true, false, "age,gender,smile,facialHair,glasses");
		
		Dictionary<string, string> headers = new Dictionary<string, string>();
		headers.Add("ocp-apim-subscription-key", faceSubscriptionKey);
		
		headers.Add("Content-Type", "application/octet-stream");
		headers.Add("Content-Length", imageBytes.Length.ToString());

		WWW www = new WWW(requestUrl, imageBytes, headers);
		yield return www;

//		if (!string.IsNullOrEmpty(www.error)) 
//		{
//			throw new Exception(www.error + " - " + requestUrl);
//		}

		if(!CloudWebTools.IsErrorStatus(www))
		{
			//faces = JsonConvert.DeserializeObject<Face[]>(www.text, jsonSettings);
			string newJson = "{ \"faces\": " + www.text + "}";
			FacesCollection facesCollection = JsonUtility.FromJson<FacesCollection>(newJson);
			faces = facesCollection.faces;
        }
		else
		{
			ProcessFaceError(www);
		}

		if(/**recognizeEmotions &&*/ !string.IsNullOrEmpty(emotionSubscriptionKey))
		{
			// get face rectangles
			StringBuilder faceRectsStr = new StringBuilder();

			if(faces != null)
			{
				foreach(Face face in faces)
				{
					FaceRectangle rect = face.faceRectangle;
					faceRectsStr.AppendFormat("{0},{1},{2},{3};", rect.left, rect.top, rect.width, rect.height);
				}

				if(faceRectsStr.Length > 0)
				{
					faceRectsStr.Remove(faceRectsStr.Length - 1, 1); // drop the last semicolon
				}
			}

			// recognize emotions
			string emotionServiceHost = EmotionServiceHost.Replace("[location]", emotionServiceLocation);
			requestUrl = string.Format("{0}/recognize??faceRectangles={1}", emotionServiceHost, faceRectsStr);

			headers = new Dictionary<string, string>();
			headers.Add("ocp-apim-subscription-key", emotionSubscriptionKey);

			headers.Add("Content-Type", "application/octet-stream");
			headers.Add("Content-Length", imageBytes.Length.ToString());

			www = new WWW(requestUrl, imageBytes, headers);
			yield return www;

			Emotion[] emotions = null;
			if(!CloudWebTools.IsErrorStatus(www))
			{
				//emotions = JsonConvert.DeserializeObject<Emotion[]>(reader.ReadToEnd(), jsonSettings);
				string newJson = "{ \"emotions\": " + www.text + "}";
				EmotionCollection emotionCollection = JsonUtility.FromJson<EmotionCollection>(newJson);
				emotions = emotionCollection.emotions;
			}
			else
			{
				ProcessFaceError(www);
			}

			if(emotions != null)
			{
				// match the emotions to faces
				int matched = MatchEmotionsToFaces(ref faces, ref emotions);

				if(matched != faces.Length)
				{
					Debug.Log(string.Format("Matched {0}/{1} emotions to {2} faces.", matched, emotions.Length, faces.Length));
				}
			}
		}

	}


	// processes the error status in response
	private void ProcessFaceError(WWW www)
	{
		//ClientError ex = JsonConvert.DeserializeObject<ClientError>(www.text);
		ClientError ex = JsonUtility.FromJson<ClientError>(www.text);
		
		if (ex.error != null && ex.error.code != null)
		{
			string sErrorMsg = !string.IsNullOrEmpty(ex.error.code) && ex.error.code != "Unspecified" ?
				ex.error.code + " - " + ex.error.message : ex.error.message;
			throw new System.Exception(sErrorMsg);
		}
		else
		{
			//ServiceError serviceEx = JsonConvert.DeserializeObject<ServiceError>(www.text);
			ServiceError serviceEx = JsonUtility.FromJson<ServiceError>(www.text);
			
			if (serviceEx != null && serviceEx.statusCode != null)
			{
				string sErrorMsg = !string.IsNullOrEmpty(serviceEx.statusCode) && serviceEx.statusCode != "Unspecified" ?
					serviceEx.statusCode + " - " + serviceEx.message : serviceEx.message;
				throw new System.Exception(sErrorMsg);
			}
			else
			{
				throw new System.Exception("Error " + CloudWebTools.GetStatusCode(www) + ": " + CloudWebTools.GetStatusMessage(www) + "; Url: " + www.url);
			}
		}
	}
	
	
	// draw face rectangles
	/// <summary>
	/// Draws the face rectangles in the given texture.
	/// </summary>
	/// <param name="faces">List of faces.</param>
	/// <param name="tex">Tex.</param>
	/// <param name="faceColors">List of face colors for each face</param>
	/// <param name="drawHeadPoseArrow">If true, draws arrow according to head pose of each face</param>
	public void DrawFaceRects(Texture2D tex, Face[] faces, Color[] faceColors, bool drawHeadPoseArrow)
	{
		for(int i = 0; i < faces.Length; i++)
		{
			Face face = faces[i];
			Color faceColor = faceColors[i % faceColors.Length];

			FaceRectangle rect = face.faceRectangle;
			CloudTexTools.DrawRect(tex, rect.left, rect.top, rect.width, rect.height, faceColor);

			if (drawHeadPoseArrow)
			{
				HeadPose headPose = face.faceAttributes.headPose;

				int cx = rect.width / 2;
				int cy = rect.height / 4;
				int arrowX = rect.left + cx;
				int arrowY = rect.top + (3 * cy);
				int radius = Math.Min(cx, cy);

				float x = arrowX + radius * Mathf.Sin(headPose.yaw * Mathf.Deg2Rad);
				float y = arrowY + radius * Mathf.Cos(headPose.yaw * Mathf.Deg2Rad);

				int arrowHead = radius / 4;
				if (arrowHead > 15) arrowHead = 15;
				if (arrowHead < 8) arrowHead = 8;

				CloudTexTools.DrawArrow(tex, arrowX, arrowY, (int)x, (int)y, faceColor, arrowHead, 30);
			}
		}

		tex.Apply();
	}


	/// <summary>
	/// Matches the recognized emotions to faces.
	/// </summary>
	/// <returns>The number of matched emotions.</returns>
	/// <param name="faces">Array of detected Faces.</param>
	/// <param name="emotions">Array of recognized Emotions.</param>
	public int MatchEmotionsToFaces(ref Face[] faces, ref Emotion[] emotions)
	{
		int matched = 0;
		if(faces == null || emotions == null)
			return matched;

		foreach(Emotion emot in emotions)
		{
			FaceRectangle emotRect = emot.faceRectangle;

			for(int i = 0; i < faces.Length; i++)
			{
				if(Mathf.Abs(emotRect.left - faces[i].faceRectangle.left) <= 2 &&
					Mathf.Abs(emotRect.top - faces[i].faceRectangle.top) <= 2)
				{
					faces[i].emotion = emot;
					matched++;
					break;
				}
			}
		}

		return matched;
	}


}
