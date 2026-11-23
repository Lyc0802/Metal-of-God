#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 城堡入口的多路線結構（取代舊的單一長廊）：
///
///   z=104 ┌──────────┐ 前室 → Boss 房
///   z= 94 │ ▓▓大門▓▓ │
///         │ 典獄長    │
///   z= 76 │ 中央走道  ├─────────┐
///         │ (直通、    │ 西側     東側 │  西翼＝兵器庫（哨兵、投擲兵）
///         │  最危險)   │ 兵器庫   迴廊 │  東翼＝迴廊（高台、埋伏者、巡邏）
///   z= 48 ├──┐    ┌──┤                │  兩翼各有側門通向中央走道（z=70），可以繞過中央的巡邏守衛
///         │    中庭（樞紐）            │
///   z= 13 │  兩組守衛繞圈巡邏、四個埋伏者  │
///   z= 12 └──┐ 入口 ┌──┘
///   z=  0    │ 起點 │
///
/// 路線：中庭有三個出口——西通道（→兵器庫）、東通道（→迴廊）、中央大門（→中央走道，最短但最危險）。
/// 兩翼都有側門連到中央走道的後半段，所以可以走翼樓繞過中央的巡邏，直接面對大門前的典獄長。
/// </summary>
public static partial class CastleEntranceBuilder
{
    private const float DoorHeight = 5.5f;
    private static Transform routes;

    // ------------------------------------------------------------------ structure

    private static void Slab(string name, float x0, float x1, float z0, float z1)
    {
        float cx = (x0 + x1) * .5f, cz = (z0 + z1) * .5f, w = x1 - x0, l = z1 - z0;
        Box(name + " Floor", structure, new Vector3(cx, -.5f, cz), new Vector3(w, 1f, l), floorMat);
        Box(name + " Ceiling", structure, new Vector3(cx, H + .5f, cz), new Vector3(w, 1f, l), stone);
        for (float z = z0 + 3; z < z1 - .5f; z += 4)
        {
            Box("Floor Seam", structure, new Vector3(cx, .011f, z), new Vector3(w - 1f, .02f, .12f), trim, false);
        }
    }

    /// <summary>沿 Z 方向的牆（固定 x）。gaps 是 (中心 z, 寬度) 成對的開口（門洞），門洞上方有牆楣。</summary>
    private static void WallX(float x, float z0, float z1, params float[] gaps)
    {
        BuildWall(true, x, z0, z1, gaps);
    }

    /// <summary>沿 X 方向的牆（固定 z）。gaps 是 (中心 x, 寬度) 成對的開口。</summary>
    private static void WallZ(float z, float x0, float x1, params float[] gaps)
    {
        BuildWall(false, z, x0, x1, gaps);
    }

    private static void BuildWall(bool alongZ, float fixedCoord, float a0, float a1, float[] gaps)
    {
        var cuts = new List<Vector2>();
        for (int i = 0; i + 1 < gaps.Length; i += 2)
        {
            cuts.Add(new Vector2(gaps[i] - gaps[i + 1] * .5f, gaps[i] + gaps[i + 1] * .5f));
        }

        cuts.Sort((a, b) => a.x.CompareTo(b.x));
        float cursor = a0;
        foreach (var cut in cuts)
        {
            WallPiece(alongZ, fixedCoord, cursor, cut.x, 0, H);
            WallPiece(alongZ, fixedCoord, cut.x, cut.y, DoorHeight, H);
            cursor = cut.y;
        }

        WallPiece(alongZ, fixedCoord, cursor, a1, 0, H);
    }

    private static void WallPiece(bool alongZ, float fixedCoord, float s, float e, float y0, float y1)
    {
        if (e - s < .01f)
        {
            return;
        }

        float mid = (s + e) * .5f, length = e - s, height = y1 - y0, cy = (y0 + y1) * .5f;
        Vector3 center = alongZ ? new Vector3(fixedCoord, cy, mid) : new Vector3(mid, cy, fixedCoord);
        Vector3 size = alongZ ? new Vector3(1f, height, length) : new Vector3(length, height, 1f);
        Box("Wall", structure, center, size, stone);
    }

    private static void BuildStructure()
    {
        // 地板與天花板（牆的底下也要有地板，門洞才走得過去）。
        Slab("Vestibule+Courtyard", -23, 23, 0, 48);
        Slab("West Wing", -23, -5, 48, 77);
        Slab("East Wing", 5, 23, 48, 77);
        Slab("Center", -6, 6, 48, 104);
        Box("Start Wall", structure, new Vector3(0, H * .5f, -1), new Vector3(14, H, 2), stone);
        Box("End Wall", structure, new Vector3(0, H * .5f, EndZ + 1), new Vector3(14, H, 2), stone);

        // 入口前廳（z 0~12，x ±6）→ 中庭（z 13~47，x ±22）。
        WallX(-6.5f, 0, 12.5f);
        WallX(6.5f, 0, 12.5f);
        WallZ(12.5f, -23, 23, 0, 5);
        WallX(-22.5f, 12.5f, 77);
        WallX(22.5f, 12.5f, 77);
        // 中庭北牆的三個出口：西通道、中央大門、東通道。
        WallZ(47.5f, -23, 23, -14, 5, 0, 6, 14, 5);
        // 中央走道的兩側牆（兼翼樓的隔牆），z=70 各有一個側門通向翼樓；走道盡頭是前室。
        WallX(-5.5f, 48, 104, 70, 4);
        WallX(5.5f, 48, 104, 70, 4);
        // 翼樓的北牆。
        WallZ(76.5f, -23, -5, new float[0]);
        WallZ(76.5f, 5, 23, new float[0]);
    }

    /// <summary>沿 z 方向的火把（牆在 x = side*W，面向房間內側）：side=+1 右牆、-1 左牆；W 是牆內側離中線的距離。</summary>
    private static void WallTorch(float side, float wallX, float z, float y = 3.2f)
    {
        Torch(new Vector3(side * (wallX - .4f), y, z), side);
    }

    /// <summary>牆在 x = wallX、房間在牆的 +x 側（翼樓靠近中央走道的那面牆）的火把。</summary>
    private static void WallTorchFacingEast(float wallX, float z, float y = 3.2f)
    {
        Torch(new Vector3(wallX + .4f, y, z), -1);
    }

    /// <summary>牆在 x = wallX、房間在牆的 -x 側的火把。</summary>
    private static void WallTorchFacingWest(float wallX, float z, float y = 3.2f)
    {
        Torch(new Vector3(wallX - .4f, y, z), 1);
    }

    // ------------------------------------------------------------------ vestibule + courtyard

    private static void BuildVestibule()
    {
        MetalBrazier(new Vector3(-3.5f, 0, 3));
        MetalBrazier(new Vector3(3.5f, 0, 3));
        Box("Sealed Gate", props, new Vector3(0, 3.5f, .35f), new Vector3(7, 7, .7f), doorMat);
        Box("Sealed Gate Bar", props, new Vector3(0, 3.5f, .72f), new Vector3(7.4f, .35f, .15f), darkSteel, false);
        WallTorch(-1, 6, 9);
        WallTorch(1, 6, 9);
        // 入口的金幣桌：金幣袋是最基本的聲東擊西道具（Steel 推向遠處，撞到東西會響，把守衛引開）。
        Box("Coin Table", props, new Vector3(-4.2f, .45f, 8), new Vector3(1.8f, .9f, 1f), wood);
        CoinPurse(new Vector3(-4.5f, 1.04f, 8));
        CoinPurse(new Vector3(-3.9f, 1.04f, 8));
        CoinPurse(new Vector3(-4.2f, 1.04f, 8.3f));
    }

    private static void BuildCourtyard()
    {
        // 柱子方陣：提供掩體，也切開守衛的視線。
        foreach (float x in new[] { -12f, -4f, 4f, 12f })
        {
            foreach (float z in new[] { 20f, 30f, 40f })
            {
                Pillar(new Vector3(x, 0, z));
            }
        }

        // 中央的光池：兩盆火把四周照亮（站在這裡很顯眼），推倒火盆就變暗。
        MetalBrazier(new Vector3(-1.8f, 0, 30));
        MetalBrazier(new Vector3(1.8f, 0, 30));
        // 通道與大門口的火把（光池）。
        WallTorch(-1, 22, 36);
        WallTorch(1, 22, 36);
        WallTorch(-1, 22, 20);
        WallTorch(1, 22, 20);
        // 吊燈陷阱：掛在兩組守衛繞圈的必經路線上。
        Chandelier(new Vector3(0, 5.8f, 24), 4f);
        Chandelier(new Vector3(0, 5.8f, 37), 4f);
        // 散落的金幣袋與鐵桶（金幣引開守衛、鐵桶推進人堆）。
        CoinPurse(new Vector3(-15f, .12f, 19));
        CoinPurse(new Vector3(15f, .12f, 22));
        CoinPurse(new Vector3(-7f, .12f, 43));
        CoinPurse(new Vector3(7f, .12f, 16));
        Barrel(new Vector3(-8f, .5f, 34));
        Barrel(new Vector3(8.5f, .5f, 26));
        Barrel(new Vector3(-19.5f, .5f, 24));
        Barrel(new Vector3(19.5f, .5f, 36));
        // 掩體箱子（暗角）。
        Box("Crate", props, new Vector3(-17f, .9f, 28), new Vector3(1.8f, 1.8f, 1.8f), wood);
        Box("Crate", props, new Vector3(-17f, .9f, 29.9f), new Vector3(1.8f, 1.8f, 1.8f), wood);
        Box("Crate", props, new Vector3(17f, .9f, 32), new Vector3(1.8f, 1.8f, 1.8f), wood);
        Box("Crate", props, new Vector3(17f, 2.7f, 32), new Vector3(1.8f, 1.8f, 1.8f), wood);
        // 兩側外牆上的擺飾刀（沿外圈巡邏的守衛背後）。
        BladeDisplay(-1, 22, 30);
        BladeDisplay(-1, 22, 40);
        BladeDisplay(1, 22, 24);
        BladeDisplay(1, 22, 38);
    }

    // ------------------------------------------------------------------ west wing: armory

    private static void BuildWestWing()
    {
        foreach (float z in new[] { 54f, 62f, 70f })
        {
            Pillar(new Vector3(-15f, 0, z));
        }

        // 兵器桌與桌上的輕金屬（可推拉的武器 / 盾牌）。
        foreach (float z in new[] { 54f, 70f })
        {
            Box("Weapon Table", props, new Vector3(-21.2f, .55f, z), new Vector3(1.6f, 1.1f, 5f), wood);
            for (int i = 0; i < 3; i++)
            {
                var sword = Box("Loose Sword", interactives, new Vector3(-21.2f + (i - 1) * .35f, 1.2f, z + (i - 1) * 1.3f), new Vector3(.14f, .06f, 1.3f), steel);
                DynamicBody(sword, 8);
                var swordProp = MakeProp(sword, 8, 16, 14, .7f, 1.3f, 10);
                swordProp.AlignToTravel = true;
            }

            var plate = Cylinder("Loose Shield", interactives, new Vector3(-21.2f, 1.2f, z + 1.9f), .5f, .12f, steel);
            FixCylinderCollider(plate);
            DynamicBody(plate, 12);
            MakeProp(plate, 12, 12, 16, .8f, 1f, 8);
            CoinPurse(new Vector3(-21.2f, 1.18f, z - 1.9f));
        }

        // 西牆的擺飾刀：哨兵站在牆邊，背後就是刀。
        BladeDisplay(-1, 22, 58);
        BladeDisplay(-1, 22, 66);
        BladeDisplay(-1, 22, 73f);
        // 牆上的大型鐵格柵：很重，是玩家拉著自己橫移的錨點。
        foreach (float z in new[] { 61f, 69f })
        {
            var grate = Box("Iron Grate", props, new Vector3(-21.7f, 4.6f, z), new Vector3(.4f, 4f, 4f), darkSteel);
            MakeMetal(grate, 10000, false);
        }

        // 吊燈：掛在哨兵通往東側掩體的必經路上（激怒他之後，他會一路衝過這裡）。
        Chandelier(new Vector3(-11f, 5.8f, 62), 4.5f);
        // 掩體與鐵桶。
        Box("Crate", props, new Vector3(-9f, .9f, 57), new Vector3(1.8f, 1.8f, 1.8f), wood);
        Box("Crate", props, new Vector3(-9f, .9f, 58.9f), new Vector3(1.8f, 1.8f, 1.8f), wood);
        Box("Crate", props, new Vector3(-8.6f, .9f, 65.5f), new Vector3(1.8f, 1.8f, 1.8f), wood);
        Barrel(new Vector3(-15f, .5f, 50.2f));
        Barrel(new Vector3(-13.8f, .5f, 51.2f));
        Barrel(new Vector3(-9.5f, .5f, 74.2f));
        Barrel(new Vector3(-8.4f, .5f, 74.8f));
        MetalBrazier(new Vector3(-20.5f, 0, 50.6f));
        MetalBrazier(new Vector3(-7.4f, 0, 74.4f));
        // 側門（z=70）兩側的火把。
        WallTorchFacingWest(-6, 66);
        WallTorchFacingWest(-6, 74.2f);
        // 側門口的擺飾刀（站在側門前的投擲兵背後）。
        BladeDisplay(1, -6, 74.5f);
    }

    // ------------------------------------------------------------------ east wing: gallery

    private static void BuildEastWing()
    {
        // 高台 + 坡道：投擲兵站在上面俯射。
        Box("Ledge", structure, new Vector3(20.25f, 1.2f, 64.5f), new Vector3(3.5f, 2.4f, 20f), trim);
        var ramp = Box("Ledge Ramp", structure, new Vector3(20.25f, 1.0f, 51.4f), new Vector3(3.5f, .4f, 7f), trim);
        ramp.transform.rotation = Quaternion.Euler(-20.1f, 0, 0);
        // 高台上的欄杆柱（裝飾）。
        for (float z = 55; z <= 74; z += 4.75f)
        {
            Box("Ledge Post", props, new Vector3(18.7f, 2.9f, z), new Vector3(.25f, 1f, .25f), darkSteel);
        }

        foreach (float z in new[] { 60f, 66f })
        {
            Pillar(new Vector3(13f, 0, z));
        }

        Chandelier(new Vector3(15f, 5.8f, 63), 4.5f);
        // 掩體：靠側門的一排箱子。
        Box("Crate", props, new Vector3(11f, .9f, 58), new Vector3(1.8f, 1.8f, 1.8f), wood);
        Box("Crate", props, new Vector3(11f, .9f, 59.9f), new Vector3(1.8f, 1.8f, 1.8f), wood);
        Box("Crate", props, new Vector3(15.5f, .9f, 51.5f), new Vector3(1.8f, 1.8f, 1.8f), wood);
        Barrel(new Vector3(9f, .5f, 62f));
        Barrel(new Vector3(9.9f, .5f, 63.1f));
        Barrel(new Vector3(16f, .5f, 74f));
        CoinPurse(new Vector3(8f, .12f, 55));
        CoinPurse(new Vector3(17f, .12f, 70));
        MetalBrazier(new Vector3(8.2f, 0, 50.6f));
        MetalBrazier(new Vector3(17f, 0, 51f));
        // 與中央走道相鄰的牆面（x=6）：火把與擺飾刀。
        WallTorchFacingEast(6, 56);
        WallTorchFacingEast(6, 66);
        BladeDisplay(-1, -6, 60);
        BladeDisplay(-1, -6, 73.5f);
    }

    // ------------------------------------------------------------------ center corridor + approach

    private static void BuildCorridor()
    {
        // 中央走道（z 48~94，x ±5）：地毯、火盆、雕像。前半段有守衛來回巡邏，後半段是典獄長的大門前。
        Box("Carpet", props, new Vector3(0, .02f, 71), new Vector3(2.4f, .04f, 46), Mat("CastleCarpet", new Color(.25f, .04f, .05f), .1f, 0), false);
        MetalBrazier(new Vector3(-3.6f, 0, 52));
        MetalBrazier(new Vector3(3.6f, 0, 52));
        MetalBrazier(new Vector3(-3.6f, 0, 64));
        MetalBrazier(new Vector3(3.6f, 0, 64));
        Chandelier(new Vector3(0, 5.8f, 58), 4f);
        Box("Crate", props, new Vector3(3.9f, .9f, 59), new Vector3(1.8f, 1.8f, 1.8f), wood);
        Box("Crate", props, new Vector3(-3.9f, .9f, 57), new Vector3(1.8f, 1.8f, 1.8f), wood);
        // 通往大門的兩排審判者雕像。
        for (int i = 0; i < 3; i++)
        {
            float z = 80f + i * 5f;
            Statue(new Vector3(-3.6f, 0, z), 90);
            Statue(new Vector3(3.6f, 0, z), -90);
        }

        // 大門前的兩盆火：照亮守衛，推倒後門前會暗下來。
        MetalBrazier(new Vector3(-2.2f, 0, DoorZ - 3));
        MetalBrazier(new Vector3(2.2f, 0, DoorZ - 3));
        // 哨站：兩側牆上的擺飾刀正好在投擲兵背後（站在雕像之間貼牆的位置）。
        BladeDisplay(-1, 5, 87.5f);
        BladeDisplay(1, 5, 87.5f);
        // 紅地毯上方的吊燈：典獄長被激怒衝過來時，會正好經過這裡。
        Chandelier(new Vector3(0, 5.6f, 82f), 4.5f);
    }

    // ------------------------------------------------------------------ patrol routes

    /// <summary>建立一條巡邏路線（循環）。路徑點放在場景根目錄的 "Patrol Routes" 底下（不會被烘進 NavMesh）。</summary>
    private static Transform[] Route(string name, params Vector3[] points)
    {
        if (!routes)
        {
            routes = new GameObject("Patrol Routes").transform;
        }

        var holder = new GameObject(name).transform;
        holder.SetParent(routes, false);
        var result = new Transform[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var point = new GameObject(name + " " + (i + 1)).transform;
            point.SetParent(holder, false);
            point.position = points[i];
            result[i] = point;
        }

        return result;
    }
}
#endif
