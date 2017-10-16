using UnityEngine;
using System.Collections.Generic;

public class CustomShadows : MonoBehaviour {

    public enum Shadows
    {
        NONE, PCF, MV, HARD
    }

    public Shader _depthShader;
    public int _resolution = 1024;
    
    Camera _shadowCam;
    public RenderTexture _colorTarget;
    public RenderTexture _backColorTarget;
    public RenderTexture _shadowMapTexture;

    public int _iterations = 1;
    public ComputeShader _blur;

    public Shadows _shadowType = Shadows.PCF;

    private void Awake()
    {
        _depthShader = _depthShader ?? Shader.Find("Hidden/CustomShadows/Depth");

        // A regular render texture
        _colorTarget = new RenderTexture(_resolution, _resolution, 24, RenderTextureFormat.RGFloat);
        _colorTarget.filterMode = FilterMode.Bilinear;
        _colorTarget.enableRandomWrite = true;
        _colorTarget.Create();

        // Make a backbuffer version of it for MVM blur
        _backColorTarget = new RenderTexture(_resolution, _resolution, 0, RenderTextureFormat.RGFloat);
        _backColorTarget.filterMode = FilterMode.Bilinear;
        _backColorTarget.enableRandomWrite = true;
        _backColorTarget.Create();

        // Make a "shadowmap" version so it can be sampled
        _shadowMapTexture = new RenderTexture(_resolution, _resolution, 24, RenderTextureFormat.Shadowmap);
        _shadowMapTexture.filterMode = FilterMode.Bilinear;
        _shadowMapTexture.enableRandomWrite = true;
        _shadowMapTexture.Create();

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
        // Grab the light and render the scene
        Light l = FindObjectOfType<Light>();
        _shadowCam.transform.position = l.transform.position;
        _shadowCam.transform.rotation = l.transform.rotation;
        _shadowCam.transform.LookAt(_shadowCam.transform.position + _shadowCam.transform.forward, _shadowCam.transform.up);
        UpdateShadowCamera();


        RenderTexture target = null;
        if(_shadowType == Shadows.NONE)
        {
            Shader.DisableKeyword("MV_SHADOWS");
            Shader.DisableKeyword("PCF_SHADOWS");
            Shader.DisableKeyword("HARD_SHADOWS");
        }
        else if( _shadowType == Shadows.PCF)
        {
            target = _shadowMapTexture;
            Shader.DisableKeyword("MV_SHADOWS");
            Shader.DisableKeyword("HARD_SHADOWS");

            Shader.EnableKeyword("PCF_SHADOWS");
        }
        else if( _shadowType == Shadows.HARD)
        {
            target = _colorTarget;
            Shader.DisableKeyword("MV_SHADOWS");
            Shader.DisableKeyword("PCF_SHADOWS");

            Shader.EnableKeyword("HARD_SHADOWS");
        }
        else if ( _shadowType == Shadows.MV)
        {
            target = _colorTarget;
            Shader.DisableKeyword("PCF_SHADOWS");
            Shader.DisableKeyword("HARD_SHADOWS");

            Shader.EnableKeyword("MV_SHADOWS");
        }

        if (target == null)
        {
            return;
        }

        _shadowCam.targetTexture = target;
        _shadowCam.RenderWithShader(_depthShader, "");

        if (_shadowType == Shadows.MV)
        {
            // Blur the textures, swapping the buffers for MVM shadows
            RenderTexture toBlur = _colorTarget;
            RenderTexture backBlur = _backColorTarget;
            for (int i = 0; i < _iterations; i++)
            {
                _blur.SetTexture(0, "Read", toBlur);
                _blur.SetTexture(0, "Result", backBlur);
                _blur.Dispatch(0, _colorTarget.width / 8, _colorTarget.height / 8, 1);

                RenderTexture temp = toBlur;
                toBlur = backBlur;
                backBlur = temp;
            }
            target = backBlur;
        }

        // Set the qualities of the textures
        Shader.SetGlobalTexture("_ShadowTex", target);
        Shader.SetGlobalMatrix("_LightMatrix", _shadowCam.transform.worldToLocalMatrix);

        Vector4 size = Vector4.zero;
        size.y = _shadowCam.orthographicSize * 2;
        size.x = _shadowCam.aspect * size.y;
        size.z = _shadowCam.farClipPlane;
        Shader.SetGlobalVector("_ShadowTexScale", size);
    }

    // Update the camera view to encompass the geometry it will draw
    void UpdateShadowCamera()
    {
        Vector3 center, extents;
        List<Renderer> renderers = new List<Renderer>();
        renderers.AddRange(FindObjectsOfType<Renderer>());

        GetRenderersExtents(renderers, _shadowCam.transform, out center, out extents);

        center.z -= extents.z / 2;
        _shadowCam.transform.position = _shadowCam.transform.TransformPoint(center);
        _shadowCam.nearClipPlane = 0;
        _shadowCam.farClipPlane = extents.z;

        _shadowCam.aspect = extents.x / extents.y;
        _shadowCam.orthographicSize = extents.y / 2;
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

    // Disable the shadows
    void OnDisable()
    {
        Shader.DisableKeyword("MV_SHADOWS");
        Shader.DisableKeyword("PCF_SHADOWS");
        Shader.DisableKeyword("HARD_SHADOWS");
    }

    void OnDestroy() { OnDisable(); }
}
