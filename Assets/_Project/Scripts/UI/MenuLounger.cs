using RocketWars.Core;
using UnityEngine;

namespace RocketWars.UI
{
    /// <summary>
    /// Красный солдат рядом с читающим (сцена меню): лежит на спине, голова прислонена к боку читающего,
    /// ноги закинуты одна на другую, в тёмных очках, жуёт стебелёк цветка. Каска снята и лежит рядом в траве.
    /// Строится в координатах читающего (MenuReader): +Z — к камере, +X — левее на экране.
    /// </summary>
    public class MenuLounger
    {
        const int Team = 0;

        /// <summary>Центр головы (у бока читающего) и куда от неё вытянуто тело (к ногам), наклон корпуса (градусы).</summary>
        static readonly Vector3 HeadSpot = new Vector3(0.43f, 0f, -0.06f);
        static readonly Vector3 Along = new Vector3(1f, 0f, 0.45f);
        const float Incline = 0f;
        /// <summary>Лежит ровно на спине, лицом вверх.</summary>
        static readonly Vector3 FaceUp = Vector3.up;
        /// <summary>Насколько он вместе с каской выдвинут от читающего к камере (м).</summary>
        const float TowardCamera = 0.3f;
        /// <summary>Центр головы (без наклона) и толщина спины в координатах модели (Models.CharacterBody).</summary>
        const float HeadY = 1.75f, BackDepth = 0.17f;
        /// <summary>Голова наклонена вперёд, подбородком к груди (градусы).</summary>
        const float HeadNod = 25f;

        const float ChewSpeed = 22f;
        /// <summary>Закинутая нога и как она лениво покачивается из стороны в сторону (рад/с, градусы).</summary>
        static readonly Quaternion CrossedLeg = Quaternion.Euler(-14f, 0f, 9f);
        const float FootSpeed = 9f, FootSwing = 9f;

        static readonly Color Lens = new Color32(18, 18, 22, 255);
        static readonly Color Frame = new Color32(40, 40, 44, 255);
        static readonly Color Hair = new Color32(92, 60, 36, 255);
        static readonly Color Cornflower = new Color32(80, 130, 236, 255);

        readonly Transform _body, _stem, _legR;
        readonly Vector3 _bodyPos;
        readonly Quaternion _stemRotation;
        float _time;

        public MenuLounger(Transform parent)
        {
            var root = new GameObject("Lounger").transform;
            root.SetParent(parent, false);
            // читающий отвёрнут от камеры на Turn — направление на камеру в его координатах
            root.localPosition = Quaternion.Euler(0f, -MenuReader.Turn, 0f) * Vector3.forward * TowardCamera;
            _body = MenuSoldier.Build(root, Team, out var armL, out var armR, out var legL, out var legR, out var helmet);

            // лежит на спине: ось тела (Y модели) — от ног к голове (с подъёмом Incline), лицо (Z модели) — вверх
            Vector3 flat = -Along.normalized;
            Vector3 headDir = flat * Mathf.Cos(Incline * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(Incline * Mathf.Deg2Rad);
            var rotation = Quaternion.LookRotation(headDir, -FaceUp.normalized) * Quaternion.Euler(90f, 0f, 0f);
            Vector3 face = rotation * Vector3.forward;
            // спина лежит на земле — отсюда высота головы
            Vector3 head = HeadSpot;
            head.y = BackDepth * face.y + HeadY * headDir.y;
            _bodyPos = head - headDir * HeadY;
            _body.localPosition = _bodyPos;
            _body.localRotation = rotation;

            // ноги закинуты одна на другую, руки: левая вдоль тела, правая на животе
            legL.localRotation = Quaternion.Euler(0f, 0f, -4f);
            _legR = legR;
            _legR.localRotation = CrossedLeg;
            armL.localRotation = Quaternion.Euler(0f, 0f, -14f);
            armR.localRotation = Quaternion.Euler(-38f, 0f, -24f);

            // каска снята — лежит в траве по другую сторону от него, позади (дальше от камеры)
            helmet.SetParent(root, false);
            helmet.localPosition = new Vector3(1.6f, 0.02f, -0.75f);
            helmet.localRotation = Quaternion.Euler(-18f, 30f, 10f);

            // голова — отдельной частью, чтобы наклонить её к груди; волосы, очки и стебелёк — на ней
            var torso = _body.Find("Torso");
            var (torsoMesh, torsoMats) = Models.CharacterBody(Team, withHead: false);
            torso.GetComponent<MeshFilter>().sharedMesh = torsoMesh;
            torso.GetComponent<MeshRenderer>().sharedMaterials = torsoMats;
            var (headMesh, headMats) = Models.CharacterHead();
            var headPart = BoxModel.Spawn(headMesh, headMats, _body, new Vector3(0f, Models.NeckY, 0f), "Head").transform;
            headPart.localRotation = Quaternion.Euler(HeadNod, 0f, 0f);
            var neck = new Vector3(0f, -Models.NeckY, 0f);

            var (hairMesh, hairMats) = new BoxModel()
                .Box(new Vector3(0f, 1.965f, -0.02f), new Vector3(0.42f, 0.05f, 0.44f), Hair)
                .Box(new Vector3(0f, 1.85f, -0.205f), new Vector3(0.42f, 0.2f, 0.03f), Hair)
                .Box(new Vector3(0f, 1.93f, 0.205f), new Vector3(0.42f, 0.04f, 0.03f), Hair)
                .Build("menu_lounger_hair");
            BoxModel.Spawn(hairMesh, hairMats, headPart, neck, "Hair");

            // тёмные очки: два квадратных стекла в оправе, перемычка и дужки к ушам
            var (glassesMesh, glassesMats) = new BoxModel()
                .Box(new Vector3(-0.1f, 1.785f, 0.215f), new Vector3(0.15f, 0.1f, 0.02f), Lens)
                .Box(new Vector3(0.1f, 1.785f, 0.215f), new Vector3(0.15f, 0.1f, 0.02f), Lens)
                .Box(new Vector3(0f, 1.835f, 0.215f), new Vector3(0.38f, 0.02f, 0.024f), Frame)
                .Box(new Vector3(0f, 1.8f, 0.215f), new Vector3(0.06f, 0.02f, 0.022f), Frame)
                .Box(new Vector3(-0.205f, 1.82f, 0.1f), new Vector3(0.015f, 0.02f, 0.23f), Frame)
                .Box(new Vector3(0.205f, 1.82f, 0.1f), new Vector3(0.015f, 0.02f, 0.23f), Frame)
                .Build("menu_lounger_glasses");
            BoxModel.Spawn(glassesMesh, glassesMats, headPart, neck, "Glasses");

            // стебелёк в уголке рта торчит почти вертикально, чуть вбок; на конце — василёк
            var (flowerMesh, flowerMats) = MenuReader.Flower(0.34f, Cornflower);
            _stem = new GameObject("Stem").transform;
            _stem.SetParent(headPart, false);
            _stem.localPosition = neck + new Vector3(0.05f, 1.63f, 0.2f);
            _stemRotation = Quaternion.LookRotation(new Vector3(0.3f, 0.42f, 0.9f), Vector3.forward) * Quaternion.Euler(90f, 0f, 0f);
            _stem.localRotation = _stemRotation;
            BoxModel.Spawn(flowerMesh, flowerMats, _stem, new Vector3(0f, -0.03f, 0f), "Flower");
        }

        public void Update(float dt)
        {
            _time += dt;
            // дышит
            _body.localPosition = _bodyPos + Vector3.up * (Mathf.Sin(_time * 1.4f + 1f) * 0.01f);
            // жуёт: стебелёк ходит вверх-вниз, а изредка неспешно перекатывается из стороны в сторону
            float chew = Mathf.Sin(_time * ChewSpeed);
            float roll = Mathf.Sin(_time * 1.7f);
            _stem.localRotation = Quaternion.Euler(chew * 5f, roll * 14f, 0f) * _stemRotation;
            // покачивает закинутой ногой
            float foot = Mathf.Sin(_time * FootSpeed);
            _legR.localRotation = Quaternion.Euler(Mathf.Abs(foot) * -3f, 0f, foot * FootSwing) * CrossedLeg;
        }
    }
}
