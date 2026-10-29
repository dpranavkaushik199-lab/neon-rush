using System.Collections.Generic;
using System.Linq;
using NeonRunner;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// Level design built on the fast parkour system. Every room is one parkour "segment" that asks
/// for one move (or a combination), sized from the measured movement metrics so it's hard but fair:
///  - run 13 m/s, Flow top speed 19 (after ~1.4 s of unbroken running), dash 30 m/s for 0.14 s;
///  - flat jump ~0.6 s in the air: ~7.8 m at run speed, ~11 m at full Flow, ~12-13 with a dash;
///  - slide long-jump ~10-11 m; vault clears 1 m cover with no slowdown;
///  - wall run: ~5 m up a wall from run speed, ~8 m from full Flow, then 4 climb jumps of ~2.7 m;
///  - slide tunnels are 1.05 m tall (standing is 1.4, sliding 0.7).
/// The Tutorial strings the segments together one move at a time with hints; Chaos Mode draws a
/// random run of hard segments with the Security Wall chasing from the first step, armed cops,
/// ambush drones, lasers, gusts and a storm on top.
/// </summary>
public static partial class NeonRunner25DBuilder
{
    enum Kind
    {
        Sprint, Gaps, Vaults, SlideTunnel, LongJump, WallRun, WallChain, Drop, BreakRun,
        Crumble, Hooks, Sling, Zips, LaserRun, Gusts, Patrol,
    }

    // Build cursor: the ground continues at height gy from x = gx.
    static float gx, gy, segMinY, segMaxY;
    static float levelD;
    static bool armedCops;

    /// <summary>Seed for the Chaos layout; the "Reroll Chaos" menu item changes it.</summary>
    static int chaosSeed = 777;

    static LevelSpec TutorialSpec => new LevelSpec
    {
        name = "Tutorial", scene = "Tutorial", fog = new Color(0.07f, 0.1f, 0.115f), ambient = new Color(0.28f, 0.3f, 0.3f),
        spawn = new Vector2(2.5f, 3f), build = BuildTutorial,
    };

    static LevelSpec ChaosSpec => new LevelSpec
    {
        name = "Chaos Mode", scene = "Chaos", fog = new Color(0.13f, 0.04f, 0.06f), ambient = new Color(0.32f, 0.22f, 0.24f),
        spawn = new Vector2(2.5f, 3f), build = BuildChaos, seed = chaosSeed,
    };

    static void ResetLevelFeatures()
    {
        armedCops = false;
        levelD = 0f;
    }

    // ================================================================ Tutorial

    /// <summary>One move per room, each introduced safely with a hint, then a short exam.</summary>
    static void BuildTutorial()
    {
        BeginRun();
        (Kind kind, string hint)[] lessons =
        {
            (Kind.Sprint, "A/D run  ·  keep moving to build FLOW (bottom left)  ·  full Flow = top speed"),
            (Kind.Gaps, "SPACE / Z jump  ·  hold for distance  ·  jump at speed to clear wide gaps"),
            (Kind.Vaults, "Run into LOW COVER to VAULT it  ·  you keep all your speed"),
            (Kind.SlideTunnel, "Hold S / DOWN while running to SLIDE  ·  slide under low gaps"),
            (Kind.LongJump, "SLIDE, then JUMP out of it = LONG JUMP"),
            (Kind.WallRun, "Sprint into a tall wall while holding toward it = WALL RUN  ·  jump to climb higher"),
            (Kind.WallChain, "Wall-jump to the other wall: every new wall gives you another wall run"),
            (Kind.BreakRun, "SHIFT / X: DASH in 8 directions  ·  dash, slide or full Flow smash cracked walls"),
            (Kind.Drop, "Hold DOWN in the air to fast-fall  ·  landings keep your speed"),
            (Kind.Hooks, "Hold E / RB to GRAPPLE  ·  jump to let go  ·  chain swings in the air"),
            (Kind.Sling, "Grapple, then DASH right as the pull peaks = SLING-BOOST"),
            (Kind.Crumble, "Crumbling roofs fall shortly after you land  ·  don't stop"),
        };

        int index = 0;
        foreach (var (kind, hint) in lessons)
            BuildRoom($"T-{++index} {kind}", kind, 0, hint);

        // Exam: the moves back to back at tier 1.
        foreach (var kind in new[] { Kind.Vaults, Kind.LongJump, Kind.WallRun })
            BuildRoom($"T-{++index} Exam {kind}", kind, 1, index == lessons.Length + 1 ? "Final run: chain it all without stopping" : null);

        EndRun($"T-{index + 1} Exit");
    }

    // =================================================================== Chaos

    // Patterns that work with a Security Wall behind you (nothing that makes you wait).
    static readonly Kind[] ChaosPool =
    {
        Kind.Gaps, Kind.Vaults, Kind.SlideTunnel, Kind.LongJump, Kind.WallRun, Kind.WallChain, Kind.Drop,
        Kind.BreakRun, Kind.Crumble, Kind.Hooks, Kind.Sling, Kind.Zips, Kind.LaserRun, Kind.Gusts, Kind.Patrol,
    };

    const int ChaosRooms = 14;

    static void BuildChaos()
    {
        BeginRun();
        levelD = 1f;
        armedCops = true;

        float startX = gx, startY = gy;
        var plan = PlanChaos();
        for (int i = 0; i < plan.Length; i++)
        {
            float x0 = gx, y0 = gy;
            int tier = Mathf.Clamp(1 + i * 3 / ChaosRooms, 1, 3);
            BuildRoom($"C-{i + 1} {plan[i]}", plan[i], tier, i == 0 ? "CHAOS: the wall never stops  ·  keep your FLOW" : null, roomStart =>
            {
                // Chaos twists layered on top of the room.
                bool hasCops = plan[i] == Kind.Patrol;
                if (i > 0 && !hasCops && Random.value < 0.45f) AmbushDrone(x0, gx, segMaxY + 4f);
                if (Random.value < 0.6f) PoliceBar(new Vector3(x0 + 2.5f, y0 + 0.1f, 1.6f));
            });
        }
        EndRun($"C-{plan.Length + 1} Exit");

        // The Security Wall is on you from the first step; it slows to a crawl only if you're far ahead.
        var wall = SecurityWall(startX - 16f, startX + 6f, startY + 10f);
        Set(wall, "speed", 10.5f);
        Set(wall, "catchUpSpeed", 19f);
        Set(wall, "catchUpDistance", 28f);
        Set(wall, "respawnGap", 18f);
        AddStorm();
    }

    /// <summary>Random run of segments: never the same twice in a row, nothing repeated until the pool runs dry.</summary>
    static Kind[] PlanChaos()
    {
        var plan = new Kind[ChaosRooms];
        var bag = new List<Kind>();
        Kind last = (Kind)(-1);
        for (int i = 0; i < ChaosRooms; i++)
        {
            if (bag.Count == 0) bag.AddRange(ChaosPool);
            var options = bag.Where(k => k != last).ToList();
            if (options.Count == 0) options = ChaosPool.Where(k => k != last).ToList();
            var kind = options[Random.Range(0, options.Count)];
            bag.Remove(kind);
            plan[i] = last = kind;
        }
        return plan;
    }

    // ================================================================ Assembly

    static void BeginRun()
    {
        gx = 0f;
        gy = 2f;
        Block(-2f, gy - 12f, 0f, gy + 22f);
    }

    static void BuildRoom(string name, Kind kind, int tier, string hint, System.Action<float> twist = null)
    {
        float x0 = gx, y0 = gy;
        segMinY = segMaxY = gy;
        if (!string.IsNullOrEmpty(hint)) Hint(x0 + 3f, y0, 4f, hint);
        BuildSegment(kind, tier);
        twist?.Invoke(x0);
        Room(name, x0, segMinY - 6f, gx, segMaxY + 12f, x0 + 1.5f, y0 + 1f);
    }

    static void EndRun(string name)
    {
        float x = gx;
        Ground(x, x + 14f, gy);
        Block(x + 14f, gy - 12f, x + 16f, gy + 24f);
        Goal(x + 11f, gy);
        Room(name, x, gy - 6f, x + 14f, gy + 16f, x + 1.5f, gy + 1f);
    }

    static void BuildSegment(Kind kind, int tier)
    {
        switch (kind)
        {
            case Kind.Sprint: SegSprint(tier); break;
            case Kind.Gaps: SegGaps(tier); break;
            case Kind.Vaults: SegVaults(tier); break;
            case Kind.SlideTunnel: SegSlideTunnel(tier); break;
            case Kind.LongJump: SegLongJump(tier); break;
            case Kind.WallRun: SegWallRun(tier); break;
            case Kind.WallChain: SegWallChain(tier); break;
            case Kind.Drop: SegDrop(tier); break;
            case Kind.BreakRun: SegBreakRun(tier); break;
            case Kind.Crumble: SegCrumble(tier); break;
            case Kind.Hooks: SegHooks(tier); break;
            case Kind.Sling: SegSling(tier); break;
            case Kind.Zips: SegZips(tier); break;
            case Kind.LaserRun: SegLaserRun(tier); break;
            case Kind.Gusts: SegGusts(tier); break;
            case Kind.Patrol: SegPatrol(tier); break;
        }
    }

    static void Track(float y)
    {
        segMinY = Mathf.Min(segMinY, y);
        segMaxY = Mathf.Max(segMaxY, y);
    }

    /// <summary>Knee-high cover (1 m): vaulted at speed, never needs a jump.</summary>
    static void Cover(float x, float groundY) => Block(x, groundY, x + 0.8f, groundY + 1f);

    // ================================================================ Segments
    // Each starts with its own runway at (gx, gy) and leaves the cursor on the ground it ends on.

    /// <summary>A long flat roof with small steps: room to build Flow.</summary>
    static void SegSprint(int tier)
    {
        float x = gx, y = gy;
        float len = 26f + 4f * tier;
        Ground(x, x + len * 0.5f, y);
        // A one-step rise halfway: hop it without breaking stride.
        Ground(x + len * 0.5f, x + len, y + 1f);
        Shard(x + len * 0.75f, y + 5f);
        gx = x + len;
        gy = y + 1f;
        Track(gy + 6f);
    }

    /// <summary>Rooftop gaps of 6 -> 11.6 m: the wide ones need Flow or a dash.</summary>
    static void SegGaps(int tier)
    {
        float x = gx, y = gy;
        Ground(x, x + 8f, y);
        x += 8f;
        int gaps = 2 + tier;
        for (int i = 0; i < gaps; i++)
        {
            float w = Random.Range(6f, 7.5f + 1.4f * tier);
            float ny = Mathf.Max(2f, y + Random.Range(y > 12f ? -3f : -1.5f, 1f));
            Pit(x, x + w, Mathf.Min(y, ny));
            float pad = Random.Range(4.5f, 6.5f) - 0.5f * tier;
            Ground(x + w, x + w + pad, ny);
            if (i == gaps / 2) Shard(x + w * 0.5f, Mathf.Max(y, ny) + 3.6f);
            x += w + pad;
            y = ny;
            Track(y);
            Track(y + 5f);
        }
        gx = x;
        gy = y;
    }

    /// <summary>Cover in the running line: vault it at speed. From tier 2, spike gaps between covers.</summary>
    static void SegVaults(int tier)
    {
        float x = gx, y = gy;
        Ground(x, x + 8f, y);
        x += 8f;
        int covers = 2 + tier;
        for (int i = 0; i < covers; i++)
        {
            bool gap = tier >= 2 && i % 2 == 1;
            if (gap)
            {
                // Vault, land, jump the pit: rhythm without slowing down.
                Ground(x, x + 3f, y);
                Pit(x + 3f, x + 8f, y);
                x += 8f;
            }
            float run = Random.Range(5.5f, 7.5f);
            Ground(x, x + run, y);
            Cover(x + run * 0.5f, y);
            if (i == covers - 1) Shard(x + run * 0.5f + 0.4f, y + 4.2f);
            x += run;
        }
        Ground(x, x + 5f, y);
        gx = x + 5f;
        Track(y + 7f);
    }

    /// <summary>A low overhang (1.05 m clearance) you have to slide under; longer and doubled later.</summary>
    static void SegSlideTunnel(int tier)
    {
        float x = gx, y = gy;
        Ground(x, x + 10f, y);
        x += 10f;
        int tunnels = tier >= 2 ? 2 : 1;
        for (int i = 0; i < tunnels; i++)
        {
            float len = 6f + 2.5f * tier;
            Ground(x, x + len, y);
            // Too tall to wall-run and climb over: the only way through is underneath.
            Block(x, y + 1.05f, x + len, y + 26f);
            x += len;
            if (i < tunnels - 1)
            {
                // A gap between tunnels: pop out, jump, slide again.
                Ground(x, x + 4f, y);
                Pit(x + 4f, x + 9f, y);
                Shard(x + 6.5f, y + 4f);
                x += 9f;
            }
        }
        if (tier >= 1)
        {
            // Exit straight into a pit: a slide-jump out of the tunnel mouth.
            Ground(x, x + 2.5f, y);
            Pit(x + 2.5f, x + 9f, y);
            x += 9f;
        }
        Ground(x, x + 5f, y);
        if (tunnels == 1) Shard(x - 3f, y + 4f);
        gx = x + 5f;
        Track(y + 15f);
    }

    /// <summary>A 9.5 -> 12 m gap from a long runway: slide-jump it (or Flow + dash). Later a node mid-gap.</summary>
    static void SegLongJump(int tier)
    {
        float x = gx, y = gy;
        Ground(x, x + 16f, y);
        x += 16f;
        float w = 9.5f + 0.8f * tier;
        if (tier >= 3)
        {
            // Two long jumps with a dash-refill node over the second.
            Pit(x, x + w, y);
            x += w;
            Ground(x, x + 6f, y);
            x += 6f;
            w += 3f;
            DataNode(x + w * 0.5f, y + 2.5f);
        }
        Pit(x, x + w, y);
        Shard(x + w * 0.5f, y + 3.2f);
        x += w;
        Ground(x, x + 5f, y);
        gx = x + 5f;
        Track(y + 6f);
    }

    /// <summary>A wall 5 -> 9 m tall at the end of a runway: wall-run it (plus climb jumps when tall). From tier 2, over spikes.</summary>
    static void SegWallRun(int tier)
    {
        float x = gx, y = gy;
        float run = 14f;
        Ground(x, x + run, y);
        x += run;
        if (tier >= 2)
        {
            // Jump into the wall over a pit: the wall run starts in the air.
            Pit(x, x + 3f, y);
            x += 3f;
        }
        float h = 5f + 1.3f * tier;
        Block(x, y - 12f, x + 8f, y + h);
        Shard(x - 1.2f, y + h + 2f);
        gx = x + 8f;
        gy = y + h;
        Track(gy + 5f);
    }

    /// <summary>
    /// A shaft between a hanging wall and a tower: wall-run the tower, kick to the hanging wall
    /// (a fresh wall run), kick back over the top. 11 -> 17 m.
    /// </summary>
    static void SegWallChain(int tier)
    {
        float x = gx, y = gy;
        float rise = 11f + 2f * tier;
        bool spikes = tier >= 2;
        Ground(x, x + (spikes ? 13f : 16f), y);
        if (spikes) Pit(x + 13f, x + 16f, y);
        // Hanging wall starts 3.5 m up so you can sprint under it into the tower.
        Block(x + 11.5f, y + 3.5f, x + 13f, y + rise + 2f);
        Block(x + 16f, y - 12f, x + 22f, y + rise);
        Shard(x + 14.5f, y + rise * 0.6f);
        gx = x + 22f;
        gy = y + rise;
        Track(gy + 5f);
    }

    /// <summary>Stepped drop across 3 -> 5 roofs: fast-fall and land running.</summary>
    static void SegDrop(int tier)
    {
        float x = gx, y = gy;
        Ground(x, x + 6f, y);
        x += 6f;
        int steps = 3 + (tier + 1) / 2;
        for (int i = 0; i < steps; i++)
        {
            float w = 4f + tier * 0.6f;
            float down = Mathf.Min(Random.Range(2.5f, 4f), Mathf.Max(0f, y - 2f));
            float ny = y - down;
            Pit(x, x + w, ny);
            float pad = 5f;
            Ground(x + w, x + w + pad, ny);
            if (i == steps / 2) Shard(x + w * 0.5f, y + 2.5f);
            x += w + pad;
            y = ny;
            Track(y);
        }
        Track(gy + 5f);
        gx = x;
        gy = y;
    }

    /// <summary>Cracked walls in the running line: slide, dash or full Flow through. Later one hangs mid-gap, then a cop behind the last.</summary>
    static void SegBreakRun(int tier)
    {
        float x = gx, y = gy;
        Ground(x, x + 14f, y);
        BreakWall(x + 12f, y, y + 7f);
        Block(x + 12f, y + 7f, x + 13.2f, y + 22f);
        x += 14f;
        if (tier >= 1)
        {
            Ground(x, x + 5f, y);
            x += 5f;
            float gap = 9f;
            Pit(x, x + gap, y);
            BreakWall(x + gap * 0.5f - 0.6f, y - 3f, y + 6f);
            Block(x + gap * 0.5f - 0.6f, y + 6f, x + gap * 0.5f + 0.6f, y + 22f);
            Shard(x + gap * 0.5f, y - 1.2f);
            x += gap;
        }
        Ground(x, x + 7f, y);
        x += 7f;
        if (tier >= 2)
        {
            BreakWall(x, y, y + 7f);
            Block(x, y + 7f, x + 1.2f, y + 22f);
            Ground(x, x + 16f, y);
            Guard(x + 10f, y, x + 5f, x + 15f);
            x += 16f;
        }
        gx = x;
        Track(y + 10f);
    }

    /// <summary>Crumbling roofs over spikes, gaps 5 -> 6.8 m: flow across without stopping.</summary>
    static void SegCrumble(int tier)
    {
        float x = gx, y = gy;
        Ground(x, x + 8f, y);
        x += 8f;
        int n = 3 + tier;
        float width = 3.2f - 0.3f * tier, gap = 5f + 0.6f * tier;
        float start = x;
        for (int i = 0; i < n; i++)
        {
            float cx = x + gap;
            Crumble(cx, y - 0.6f, cx + width, y);
            if (i == n / 2) Shard(cx + width * 0.5f, y + 3.8f);
            x = cx + width;
        }
        x += gap;
        Pit(start, x, y);
        Ground(x, x + 4f, y);
        gx = x + 4f;
        Track(y + 6f);
    }

    /// <summary>Swing chain: 1 -> 4 grapple points 9 apart at +8.</summary>
    static void SegHooks(int tier)
    {
        float x = gx, y = gy;
        Ground(x, x + 8f, y);
        x += 8f;
        int n = 1 + tier;
        float px = x + 5f;
        for (int i = 0; i < n; i++)
        {
            GrapplePoint(px, y + 8f);
            if (i < n - 1) px += 9f;
        }
        Shard((x + px) * 0.5f, y + 10.5f);
        float end = px + 8f;
        Pit(x, end, y);
        Ground(end, end + 4f, y);
        gx = end + 4f;
        Track(y + 11f);
    }

    /// <summary>Sling-Boost jump: one hook at the edge, a 24 -> 33 m gap.</summary>
    static void SegSling(int tier)
    {
        float x = gx, y = gy;
        Ground(x, x + 10f, y);
        x += 10f;
        GrapplePoint(x - 1f, y + 7.5f);
        float len = 24f + 3f * tier;
        Pit(x, x + len, y);
        Shard(x + len * 0.5f, y + 11f);
        Ground(x + len, x + len + 4f, y);
        gx = x + len + 4f;
        Track(y + 12f);
    }

    /// <summary>
    /// Zip platforms over spikes: 1 -> 3 in a row, each carrying you 7 -> 13 m (rising from tier 1),
    /// with a running hop between them. From tier 2 a laser pulses across the second zip's track.
    /// </summary>
    static void SegZips(int tier)
    {
        float x = gx, y = gy;
        Ground(x, x + 6f, y);
        x += 6f;
        float start = x;
        int n = 1 + Mathf.Min(tier, 2);
        float top = y;
        for (int i = 0; i < n; i++)
        {
            float gap = i == 0 ? 2f : 4f + 0.5f * tier;
            float px = x + gap;
            float travel = 7f + 2f * tier;
            float rise = tier >= 1 ? Random.Range(0f, 2.5f) : 0f;
            ZipPlatform(px, y, 3f, new Vector2(travel, rise));
            if (i == 1 && tier >= 2)
                Laser(px + 1.5f + travel * 0.5f, y - 3f, 10f, 0.8f, 1.6f, Random.value);
            x = px + 3f + travel;
            y += rise;
            top = Mathf.Max(top, y);
        }
        Shard(x - 1.5f, top + 4.2f);
        float end = x + 4f;
        Pit(start, end, gy);
        Ground(end, end + 4f, y);
        gx = end + 4f;
        gy = y;
        Track(top + 6f);
    }

    /// <summary>
    /// Laser rhythm corridor: 2 -> 5 curtains 9 m apart whose openings travel at Flow speed, so an
    /// unbroken sprint goes straight through; from tier 2, vault cover sits between some of them.
    /// </summary>
    static void SegLaserRun(int tier)
    {
        float x = gx, y = gy;
        int n = 2 + tier;
        const float spacing = 9f;
        float length = 12f + n * spacing + 4f;
        Ground(x, x + length, y);
        float on = 0.9f + 0.2f * tier, off = 2.4f - on;
        // ~0.6 s between gates at 15 m/s = a quarter of the 2.4 s cycle.
        for (int i = 0; i < n; i++)
        {
            float lx = x + 14f + i * spacing;
            Laser(lx, y, 10f, on, off, -i * 0.25f);
            if (tier >= 2 && i % 2 == 0 && i < n - 1) Cover(lx + spacing * 0.5f, y);
        }
        Shard(x + 14f + spacing * 0.5f, y + 9.5f);
        gx = x + length;
        Track(y + 11f);
    }

    /// <summary>A tailwind stretches a long jump over 13 -> 17.5 m; from tier 1 a pulsing headwind over a normal gap.</summary>
    static void SegGusts(int tier)
    {
        float x = gx, y = gy;
        Ground(x, x + 10f, y);
        x += 10f;
        float wide = 13f + 1.5f * tier;
        Pit(x, x + wide, y);
        Wind(Rect.MinMaxRect(x - 2f, y - 4f, x + wide, y + 9f), new Vector2(7f, 0f), tier >= 2 ? 3.2f : 0f, 0f);
        Shard(x + wide * 0.5f, y + 6.5f);
        x += wide;
        Ground(x, x + 5f, y);
        x += 5f;
        if (tier >= 1)
        {
            float gap = 7f + 0.4f * tier;
            Pit(x, x + gap, y);
            Wind(Rect.MinMaxRect(x - 3f, y - 4f, x + gap + 2f, y + 9f), new Vector2(-8f, 0f), 2.6f, 0.5f);
            x += gap;
            Ground(x, x + 5f, y);
            x += 5f;
        }
        gx = x;
        Track(y + 9f);
    }

    /// <summary>Armed patrol on a long roof: vault the cover, swing the hook overhead, or smoke/tase through.</summary>
    static void SegPatrol(int tier)
    {
        float x = gx, y = gy;
        float len = 34f + 6f * tier;
        Ground(x, x + len, y);
        int guards = 1 + tier / 2;
        float span = (len - 12f) / guards;
        for (int i = 0; i < guards; i++)
        {
            float a = x + 12f + span * i, b = a + span;
            Cover(a - 2f, y);
            GrapplePoint(a + 2f, y + 8f);
            Guard((a + b) * 0.5f, y, a + 0.5f, b - 0.5f);
        }
        Shard(x + 10f, y + 5.2f);
        gx = x + len;
        Track(y + 10f);
    }

    // ========================================================= Object builders

    static void MakeDynamic(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
    }

    static GameObject Child(GameObject parent, GameObject child)
    {
        child.transform.SetParent(parent.transform, true);
        return child;
    }

    static MovingPlatform MovingDeck(string name, MovingPlatform.Mode mode, Vector2 a, Vector2 b, float width, Color accent)
    {
        var go = new GameObject(name) { layer = groundLayer };
        go.transform.SetParent(levelRoot);
        go.transform.position = a;
        go.AddComponent<BoxCollider2D>().size = new Vector2(width, 0.5f);
        var platform = go.AddComponent<MovingPlatform>();

        var visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform, false);
        Child(visual, Box("Deck", (Vector3)a + new Vector3(0f, 0f, 0.6f), new Vector3(width, 0.5f, 2.6f), platePBR));
        Child(visual, Box("Skirt", (Vector3)a + new Vector3(0f, -0.4f, 0.6f), new Vector3(width * 0.8f, 0.3f, 2f), corrugatedPBR));
        Child(visual, NeonBar((Vector3)a + new Vector3(0f, 0.24f, -0.72f), new Vector3(width, 0.05f, 0.05f), accent));
        var light = AddLight(go.transform, new Vector3(0f, -0.6f, -0.8f), accent, 4f, 3f);
        MakeDynamic(go);

        Set(platform, "mode", (int)mode);
        Set(platform, "pointA", a);
        Set(platform, "pointB", b);
        Set(platform, "visual", visual.transform);
        Set(platform, "statusLight", light);
        return platform;
    }

    /// <summary>A rail behind a moving platform's path so its route is readable before you commit.</summary>
    static void Rail(Vector2 a, Vector2 b, Color c)
    {
        Vector2 mid = (a + b) * 0.5f;
        var rail = Box("Rail", new Vector3(mid.x, mid.y - 0.1f, 2.2f), new Vector3((b - a).magnitude + 3f, 0.12f, 0.12f), platePBR);
        rail.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
        var glow = NeonBar(new Vector3(mid.x, mid.y - 0.1f, 2.1f), new Vector3((b - a).magnitude + 3f, 0.03f, 0.03f), c * 0.4f);
        glow.transform.rotation = rail.transform.rotation;
    }

    /// <summary>Zip platform resting with its top at y, left edge at xMin.</summary>
    static void ZipPlatform(float xMin, float y, float width, Vector2 travel)
    {
        var a = new Vector2(xMin + width * 0.5f, y - 0.25f);
        Rail(a, a + travel, Mint);
        var p = MovingDeck("ZipPlatform", MovingPlatform.Mode.Zip, a, a + travel, width, Mint);
        // Chevron lights pointing along the travel direction.
        float angle = Mathf.Atan2(travel.y, travel.x) * Mathf.Rad2Deg;
        for (int i = -1; i <= 1; i++)
        {
            var chevron = NeonBar((Vector3)a + new Vector3(i * 0.7f, 0.05f, -0.75f), new Vector3(0.35f, 0.08f, 0.04f), Mint);
            chevron.transform.rotation = Quaternion.Euler(0f, 0f, angle + 35f);
            chevron.transform.SetParent(p.transform.Find("Visual"), true);
            chevron.isStatic = false;
        }
    }

    /// <summary>Vertical laser curtain from yBottom up, pulsing on/off.</summary>
    static LaserGate Laser(float x, float yBottom, float height, float onTime, float offTime, float phase)
    {
        var go = new GameObject("LaserGate");
        go.transform.SetParent(levelRoot);
        go.transform.position = new Vector3(x, yBottom + height * 0.5f, 0f);
        var killGo = new GameObject("Kill");
        killGo.transform.SetParent(go.transform, false);
        var kill = killGo.AddComponent<BoxCollider2D>();
        kill.isTrigger = true;
        kill.size = new Vector2(0.3f, height);
        killGo.AddComponent<KillZone>();

        Child(go, Box("EmitterLow", new Vector3(x, yBottom + 0.2f, 0f), new Vector3(0.7f, 0.4f, 1.6f), platePBR));
        Child(go, Box("EmitterHigh", new Vector3(x, yBottom + height - 0.2f, 0f), new Vector3(0.7f, 0.4f, 1.6f), platePBR));
        var beams = new List<Object>();
        foreach (float z in new[] { -0.5f, 0f, 0.5f })
            beams.Add(Child(go, NeonBar(new Vector3(x, yBottom + height * 0.5f, z), new Vector3(0.05f, height - 0.6f, 0.05f), Danger)).GetComponent<Renderer>());
        var glow = AddLight(go.transform, new Vector3(0f, 0f, -1.2f), Danger, 6f, 4f);
        MakeDynamic(go);

        var gate = go.AddComponent<LaserGate>();
        Set(gate, "killCollider", kill);
        Set(gate, "beams", beams.ToArray());
        Set(gate, "glow", glow);
        Set(gate, "onTime", onTime);
        Set(gate, "offTime", offTime);
        Set(gate, "phase", Mathf.Repeat(phase, 1f));
        return gate;
    }

    /// <summary>Cracked panel that a dash, boost, slide or full-Flow sprint smashes through.</summary>
    static void BreakWall(float x, float yMin, float yMax)
    {
        const float w = 1.2f;
        float h = yMax - yMin;
        var go = new GameObject("BreakableWall") { layer = groundLayer };
        go.transform.SetParent(levelRoot);
        go.transform.position = new Vector3(x + w * 0.5f, yMin + h * 0.5f, 0f);
        go.AddComponent<BoxCollider2D>().size = new Vector2(w, h);

        var visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform, false);
        var c = go.transform.position;
        Child(visual, Box("Panel", c + new Vector3(0f, 0f, 0.8f), new Vector3(w, h, 3.2f), wallPBR));
        // Glowing fault lines tell you it will give.
        for (int i = 0; i < 4; i++)
        {
            var crack = NeonBar(c + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(-h * 0.4f, h * 0.4f), -0.82f), new Vector3(0.04f, Random.Range(0.8f, 1.8f), 0.03f), Amber * 0.7f);
            crack.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-35f, 35f));
            Child(visual, crack);
        }
        MakeDynamic(go);

        var wall = go.AddComponent<BreakableWall>();
        Set(wall, "visual", visual.transform);
        Set(wall, "debrisMaterial", wallPBR);
    }

    static void Wind(Rect area, Vector2 wind, float pulse, float phase)
    {
        var go = new GameObject(wind.x >= 0f ? "Tailwind" : "Headwind");
        go.transform.SetParent(levelRoot);
        go.transform.position = area.center;
        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = area.size;
        var zone = go.AddComponent<WindZone2D>();
        Set(zone, "wind", wind);
        Set(zone, "pulsePeriod", pulse);
        Set(zone, "phase", phase);
        Set(zone, "wispSprite", wisp);
        Set(zone, "material", spriteMat);
    }

    static void PoliceBar(Vector3 pos)
    {
        var go = new GameObject("PoliceLights");
        go.transform.SetParent(decorRoot);
        go.transform.position = pos;
        var red = Box("Red", pos + new Vector3(-0.32f, 0.1f, 0f), new Vector3(0.55f, 0.16f, 0.22f), NeonMat(Color.white * 3f));
        var blue = Box("Blue", pos + new Vector3(0.32f, 0.1f, 0f), new Vector3(0.55f, 0.16f, 0.22f), NeonMat(Color.white * 3f));
        Child(go, red);
        Child(go, blue);
        var redLight = AddLight(go.transform, new Vector3(-0.4f, 0.6f, -0.6f), new Color(1f, 0.1f, 0.1f), 10f, 0f);
        var blueLight = AddLight(go.transform, new Vector3(0.4f, 0.6f, -0.6f), new Color(0.2f, 0.35f, 1f), 10f, 0f);
        MakeDynamic(go);
        var lights = go.AddComponent<PoliceLights>();
        Set(lights, "red", redLight);
        Set(lights, "blue", blueLight);
        Set(lights, "redBar", red.GetComponent<Renderer>());
        Set(lights, "blueBar", blue.GetComponent<Renderer>());
    }

    /// <summary>Armed drone sweeping high over a room: its fire is telegraphed, but it radios every cop in the level.</summary>
    static void AmbushDrone(float xMin, float xMax, float y)
    {
        var drone = Drone(new Vector2(xMin + 3f, y), new Vector2(xMax - 3f, y));
        Set(drone, "sightRange", 12f);
        Set(drone, "canShoot", true);
    }

    static void AddStorm()
    {
        var go = new GameObject("Storm");
        go.transform.SetParent(decorRoot);
        var storm = go.AddComponent<Storm>();
        Set(storm, "moon", GameObject.Find("Moonlight").GetComponent<Light>());
        Set(storm, "cameraFollow", Object.FindFirstObjectByType<CameraFollow2D>());
    }

    /// <summary>Chaos Mode cops: fast, sharp and armed, tuned against a 13-19 m/s runner.</summary>
    static void TuneCop(CopBase cop, bool isGuard)
    {
        if (!armedCops) return;
        float d = levelD;
        Set(cop, "reactionTime", Mathf.Lerp(0.22f, 0.12f, d));
        Set(cop, "loseSightTime", Mathf.Lerp(3f, 4f, d));
        Set(cop, "canShoot", true);
        Set(cop, "shootInterval", Mathf.Lerp(1.8f, 1.1f, d));
        Set(cop, "shootWindup", Mathf.Lerp(0.5f, 0.38f, d));
        Set(cop, "boltSpeed", Mathf.Lerp(20f, 26f, d));
        Set(cop, "aimLead", Mathf.Lerp(0.05f, 0.12f, d));
        Set(cop, "boltMask", (LayerMask)((1 << groundLayer) | (1 << playerLayer)));
        Set(cop, "boltSprite", square);
        Set(cop, "boltMaterial", spriteMat);
        if (isGuard)
        {
            Set(cop, "sightRange", Mathf.Lerp(9f, 11f, d));
            Set(cop, "patrolSpeed", Mathf.Lerp(3.5f, 5f, d));
            // A cop on foot can't keep up with a runner in Flow; stopping is what gets you caught.
            Set(cop, "chaseSpeed", Mathf.Lerp(9f, 12f, d));
            Set(cop, "waitAtEnds", Mathf.Lerp(0.6f, 0.3f, d));
        }
        else
        {
            Set(cop, "patrolSpeed", Mathf.Lerp(6f, 8f, d));
            Set(cop, "chaseSpeed", Mathf.Lerp(12f, 15f, d));
        }
    }
}
