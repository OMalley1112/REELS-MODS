using UnityEngine;
using UnityEngine.XR;

namespace ReelsMods
{
    /// <summary>
    /// Head + controller tracking. Keeps all XR API calls in one place so the
    /// menu code never has to care whether a headset is actually present -
    /// with no device it falls back to a static camera so the UI still runs.
    /// </summary>
    public class Rig : MonoBehaviour
    {
        public Camera cam;
        public bool xrActive;

        public Vector3 HeadPos { get; private set; }
        public Quaternion HeadRot { get; private set; }

        public Vector3 HandPos { get; private set; }
        public Quaternion HandRot { get; private set; }
        public bool RightHanded { get; set; }

        public bool LeftPrimary, LeftSecondary;
        public bool RightPrimary, RightSecondary;
        public bool RightTrigger, LeftTrigger;

        private bool _queriedHead;

        public static Rig Create()
        {
            var go = new GameObject("Rig", typeof(Rig));
            var rig = go.GetComponent<Rig>();

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(go.transform, false);
            rig.cam = camGo.AddComponent<Camera>();
            rig.cam.clearFlags = CameraClearFlags.SolidColor;
            rig.cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            rig.cam.nearClipPlane = 0.01f;
            rig.cam.farClipPlane = 200f;
            rig.cam.depth = -1;
            camGo.AddComponent<AudioListener>();

            rig.HeadPos = new Vector3(0f, 1.5f, 0f);
            rig.HeadRot = Quaternion.identity;
            rig.HandPos = rig.HeadPos + new Vector3(-0.2f, -0.3f, 0.3f);
            rig.HandRot = Quaternion.identity;
            return rig;
        }

        private void Start()
        {
            var subs = new System.Collections.Generic.List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            for (int i = 0; i < subs.Count; i++)
            {
                subs[i].TrySetTrackingOriginMode(TrackingOriginModeFlags.Device);
                subs[i].Start();
            }
        }

        private void Update()
        {
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid) head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);

            if (head.isValid)
            {
                xrActive = true;
                _queriedHead = true;
                if (head.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 hp))
                    HeadPos = hp;
                if (head.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion hr))
                    HeadRot = hr;
            }
            else if (!_queriedHead)
            {
                // no headset: park the camera at eye height facing +Z
                HeadPos = new Vector3(0f, 1.5f, 0f);
                HeadRot = Quaternion.identity;
            }

            InputDevice hand = InputDevices.GetDeviceAtXRNode(RightHanded ? XRNode.RightHand : XRNode.LeftHand);
            if (hand.isValid)
            {
                if (hand.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 wp))
                    HandPos = wp;
                if (hand.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion wr))
                    HandRot = wr;
            }

            // buttons - polled as edges by the caller
            LeftPrimary = Read(InputDevices.GetDeviceAtXRNode(XRNode.LeftHand), CommonUsages.primaryButton);
            LeftSecondary = Read(InputDevices.GetDeviceAtXRNode(XRNode.LeftHand), CommonUsages.secondaryButton);
            RightPrimary = Read(InputDevices.GetDeviceAtXRNode(XRNode.RightHand), CommonUsages.primaryButton);
            RightSecondary = Read(InputDevices.GetDeviceAtXRNode(XRNode.RightHand), CommonUsages.secondaryButton);
            RightTrigger = Read(InputDevices.GetDeviceAtXRNode(XRNode.RightHand), CommonUsages.triggerButton);
            LeftTrigger = Read(InputDevices.GetDeviceAtXRNode(XRNode.LeftHand), CommonUsages.triggerButton);

            if (cam != null)
            {
                cam.transform.localPosition = HeadPos;
                cam.transform.localRotation = HeadRot;
            }
        }

        private static bool Read(InputDevice d, InputFeatureUsage<bool> usage)
        {
            return d.isValid && d.TryGetFeatureValue(usage, out bool v) && v;
        }
    }
}
