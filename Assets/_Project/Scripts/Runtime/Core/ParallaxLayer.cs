using UnityEngine;

namespace CyberPunk.Core
{
    public class ParallaxLayer : MonoBehaviour
    {
        public float parallaxFactor; // 0 = static, 1 = moves with camera
        private Transform _cameraTransform;
        private Vector3 _lastCameraPosition;

        private void Start()
        {
            if (Camera.main != null)
            {
                _cameraTransform = Camera.main.transform;
                _lastCameraPosition = _cameraTransform.position;
            }
        }

        private void LateUpdate()
        {
            if (_cameraTransform == null) return;

            Vector3 deltaMovement = _cameraTransform.position - _lastCameraPosition;
            
            // Move the layer by a fraction of the camera movement
            // If factor is 0.5, it moves at half speed (looks far away)
            // If factor is 0, it doesn't move (looks infinite)
            // If factor is 1, it moves exactly with camera (looks like UI)
            // Usually for background we want it to move LESS than the camera to simulate depth.
            // Actually, standard parallax:
            // Far objects move slower.
            // If camera moves RIGHT (+x), background should appear to move LEFT relative to camera, 
            // but in world space it just stays put or moves slightly.
            
            // Let's use the standard "relative movement" approach
            transform.position += new Vector3(deltaMovement.x * parallaxFactor, deltaMovement.y * parallaxFactor, 0);

            _lastCameraPosition = _cameraTransform.position;
        }
    }
}
