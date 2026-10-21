using RocketWars.Player;
using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>
    /// Подключает к локальному игроку камеру, HUD и взаимодействие с пультами; при уходе — возвращает камеру
    /// на обзорную позицию (вид на оба острова, как в меню).
    /// </summary>
    public static class LocalPlayerRig
    {
        /// <summary>Цвет неба #B7D8E3.</summary>
        public static readonly Color SkyColor = new Color32(0xB7, 0xD8, 0xE3, 0xFF);
        static readonly Vector3 OverviewPosition = new Vector3(90f, 70f, -150f);

        public static Camera GetCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = SkyColor;
            if (cam.farClipPlane < 1200f)
                cam.farClipPlane = 1200f; // облака раскиданы до ~600 м
            Fx.CloudLayer.Ensure();
            return cam;
        }

        public static void Attach(NetworkPlayer player)
        {
            var cam = GetCamera();
            var controller = player.GetComponent<FpsController>();
            // камера на «штативе»: лёжа он отводит её в вид от третьего лица (DownedCamera)
            var boom = new GameObject("CameraBoom").transform;
            boom.SetParent(controller.CameraRoot, false);
            cam.transform.SetParent(boom, false);
            player.gameObject.AddComponent<DownedCamera>().boom = boom;
            player.gameObject.AddComponent<DownedGrayscale>().cam = cam;
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 75f;
            if (cam.GetComponent<Fx.CameraShake>() == null)
                cam.gameObject.AddComponent<Fx.CameraShake>();

            var interactor = player.gameObject.AddComponent<PlayerInteractor>();
            interactor.cam = cam;
            var binoculars = player.gameObject.AddComponent<Binoculars>();
            binoculars.cam = cam;
            binoculars.baseFov = cam.fieldOfView;
            player.gameObject.AddComponent<PistolView>().cam = cam;
            player.gameObject.AddComponent<RocketRider>();
            player.gameObject.AddComponent<CrateInteraction>();
            player.gameObject.AddComponent<FlagInteraction>();
        }

        public static void Detach()
        {
            var cam = Camera.main;
            if (cam != null)
                ShowOverview(cam);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public static void ShowOverview(Camera cam)
        {
            var shake = cam.GetComponent<Fx.CameraShake>();
            if (shake != null)
                Object.Destroy(shake);
            cam.transform.SetParent(null, false);
            cam.transform.position = OverviewPosition;
            cam.transform.LookAt(Vector3.zero);
            cam.fieldOfView = 60f;
        }
    }
}
