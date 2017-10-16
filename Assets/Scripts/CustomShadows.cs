﻿using UnityEngine;
using System.Collections.Generic;

public class CustomShadows : MonoBehaviour {

    public enum Shadows
    {
        NONE,
        HARD,
        VARIANCE
    }

    [Header("Initialization")]
    [SerializeField]
    Shader _depthShader;

    [SerializeField]
    int _resolution = 1024;

    [SerializeField]
    ComputeShader _blur;

    [Header("Shadow Settings")]
    public int blurIterations = 1;
    [Range(0, 1)]
    public float maxShadowIntensity = 1;
    public Shadows _shadowType = Shadows.HARD;

    // Render Targets
    Camera _shadowCam;
    RenderTexture _backTarget;
    RenderTexture _target;

    #region LifeCycle
    private void Awake()
    {
        _depthShader = _depthShader ? _depthShader : Shader.Find("Hidden/CustomShadows/Depth");

        // Create the shadow rendering camera
        GameObject go = new GameObject("shadow cam");
        _shadowCam = go.AddComponent<Camera>();
        _shadowCam.orthographic = true;
        _shadowCam.nearClipPlane = 0;
        _shadowCam.enabled = false;
        _shadowCam.backgroundColor = new Color(0, 0, 0, 0);
        _shadowCam.clearFlags = CameraClearFlags.SolidColor;
    }

    void Update ()
    {
        UpdateRenderTexture();
        UpdateShadowCameraPos();
        
        _shadowCam.targetTexture = _target;
        _shadowCam.RenderWithShader(_depthShader, "");

        if (_shadowType == Shadows.VARIANCE)
        {
            for (int i = 0; i < blurIterations; i++)
            {
                _blur.SetTexture(0, "Read", _target);
                _blur.SetTexture(0, "Result", _backTarget);
                _blur.Dispatch(0, _target.width / 8, _target.height / 8, 1);

                Swap(ref _backTarget, ref _target);
            }
        }

        UpdateShaderValues();
    }

    // Disable the shadows
    void OnDisable()
    {
        Destroy(_target);
        Destroy(_backTarget);
        _target = null;
        _backTarget = null;

        Shader.DisableKeyword("VARIANCE_SHADOWS");
        Shader.DisableKeyword("HARD_SHADOWS");
    }
    #endregion

    #region Update Functions
    void UpdateShaderValues()
    {
        ForAllKeywords(s => Shader.DisableKeyword(ToKeyword(s)));
        Shader.EnableKeyword(ToKeyword(_shadowType));

        // Set the qualities of the textures
        Shader.SetGlobalTexture("_ShadowTex", _target);
        Shader.SetGlobalMatrix("_LightMatrix", _shadowCam.transform.worldToLocalMatrix);
        Shader.SetGlobalFloat("_MaxShadowIntensity", maxShadowIntensity);

        Vector4 size = Vector4.zero;
        size.y = _shadowCam.orthographicSize * 2;
        size.x = _shadowCam.aspect * size.y;
        size.z = _shadowCam.farClipPlane;
        Shader.SetGlobalVector("_ShadowTexScale", size);
    }

    // Refresh the render target if the scale has changed
    void UpdateRenderTexture()
    {
        if (_target != null && _target.width != _resolution)
        {
            Destroy(_target);
            _target = null;
        }

        if (_target == null)
        {
            _target = CreateTarget();
            _backTarget = CreateTarget();
        }
    }

    // Update the camera view to encompass the geometry it will draw
    void UpdateShadowCameraPos()
    {
        // Update the position
        Camera cam = _shadowCam;
        Light l = FindObjectOfType<Light>();
        cam.transform.position = l.transform.position;
        cam.transform.rotation = l.transform.rotation;
        cam.transform.LookAt(cam.transform.position + cam.transform.forward, cam.transform.up);

        Vector3 center, extents;
        List<Renderer> renderers = new List<Renderer>();
        renderers.AddRange(FindObjectsOfType<Renderer>());

        GetRenderersExtents(renderers, cam.transform, out center, out extents);

        center.z -= extents.z / 2;
        cam.transform.position = cam.transform.TransformPoint(center);
        cam.nearClipPlane = 0;
        cam.farClipPlane = extents.z;

        cam.aspect = extents.x / extents.y;
        cam.orthographicSize = extents.y / 2;
    }
    #endregion

    #region Utilities
    // Creates a rendertarget
    RenderTexture CreateTarget()
    {
        RenderTexture tg = new RenderTexture(_resolution, _resolution, 24, RenderTextureFormat.ARGBFloat);
        tg.filterMode = FilterMode.Bilinear;
        tg.enableRandomWrite = true;
        tg.Create();

        return tg;
    }

    void ForAllKeywords(System.Action<Shadows> func)
    {
        func(Shadows.HARD);
        func(Shadows.VARIANCE);
    }

    string ToKeyword(Shadows en)
    {
        if (en == Shadows.HARD) return "HARD_SHADOWS";
        if (en == Shadows.VARIANCE) return "VARIANCE_SHADOWS";
        return "";
    }

    // Returns the bounds extents in the provided frame
    void GetRenderersExtents(List<Renderer> renderers, Transform frame, out Vector3 center, out Vector3 extents)
    {
        Vector3[] arr = new Vector3[8];

        Vector3 min = Vector3.one * Mathf.Infinity;
        Vector3 max = Vector3.one * Mathf.NegativeInfinity;
        foreach (var r in renderers)
        {
            GetBoundsPoints(r.bounds, arr, frame.worldToLocalMatrix);

            foreach(var p in arr)
            {
                for(int i = 0; i < 3; i ++)
                {
                    min[i] = Mathf.Min(p[i], min[i]);
                    max[i] = Mathf.Max(p[i], max[i]);
                }
            }
        }

        extents = max - min;
        center = (max + min) / 2;
    }

    // Returns the 8 points for the given bounds multiplied by
    // the given matrix
    void GetBoundsPoints(Bounds b, Vector3[] points, Matrix4x4? mat = null)
    {
        Matrix4x4 trans = mat ?? Matrix4x4.identity;

        int count = 0;
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 v = b.extents;
                    v.x *= x;
                    v.y *= y;
                    v.z *= z;
                    v += b.center;
                    v = trans.MultiplyPoint(v);

                    points[count++] = v;
                }
    }

    // Swap Elements A and B
    void Swap<T>(ref T a, ref T b)
    {
        T temp = a;
        a = b;
        b = temp;
    }
    #endregion
}
