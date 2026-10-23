using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ThreeWorlds;

internal sealed partial class Pipeline
{
    internal static string NormalSeed(string seed, int index) => index == 1 ? seed :
        (BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(seed + "|normal-world|" + index)), 0) & 0x7fffffff).ToString();
    async Task RunIndependentAsync(Options o, CancellationToken ct)
    {
        if (o.Foundations.Mode == "enabled") throw new ArgumentException("独立标准模式保持每张原生生态；额外自定义岛屿补全请使用连续母图模式。");
        await Gate.WaitAsync(ct); bool owns = false; string output = Path.GetFullPath(o.Output);
        try
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("拒绝覆盖非空目录。");
            Directory.CreateDirectory(output); owns = true; o.Output = output; WorldIO.Initialize();
            Files.Write(Path.Combine(output, "options.json"), o); File.WriteAllText(Path.Combine(output, "INCOMPLETE.txt"), "生成未完成，不可部署。");
            var specs = o.Specs(); var inputs = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                Stage?.Invoke("独立标准原生生成：" + specs[i].Name, .03 + i * .23);
                string work = Path.Combine(output, "native-" + specs[i].Role); string seed = NormalSeed(o.Seed, i);
                log($"独立标准世界 {specs[i].Name} {o.Width}×{o.Height}，实际种子={seed}，邪恶={(i == 2 ? "猩红" : "腐化")}；每张保留原生地牢/神庙/丛林。");
                await RunWorker(work, o.Width, o.Height, seed, o.Difficulty, null, ct, o, i == 2 ? 2 : 1);
                inputs.Add(Path.Combine(work, "Worlds", "SeamSource.wld"));
            }
            var inputArtifacts = inputs.Select(Files.Inspect).ToArray();
            Stage?.Invoke("统一三图海平面与80格全深度接缝", .73);
            var audits = NormalWorlds.Prepare(inputs.ToArray(), o, output, ct);
            Files.Write(Path.Combine(output, "normal-stitch-result.json"), new { inputs = inputArtifacts, audits,
                oceanPolicy = "six physical coastal water surfaces share middle world's Y; ocean columns up to220 normalized; outer80 full-depth shared natural profile; inner terrain x220..width-220 unchanged before spawn",
                layerPolicy = "same GroundLevel/RockLevel metadata, no whole-world vertical translation", objectPolicy = "relocate bounded intact coastal frame groups and standard chest metadata at sameY; audit loot/digests after save+reload; reject unmovable metadata/large structures; never silently delete/duplicate loot",
                yPolicy = "exact source player Y; no spawn/floor fallback; clipping accepted", productionTouched = false, gameE2E = "UNVERIFIED" });
            var results = new List<SliceResult>(); string worlds = Path.Combine(output, "Worlds"); Directory.CreateDirectory(worlds);
            for (int i = 0; i < 3; i++)
            {
                ct.ThrowIfCancellationRequested(); Stage?.Invoke("保存标准地图与中央出生点：" + specs[i].Name, .77 + .025 * i);
                string file = audits[i].PreparedFile;
                var result = WorldIO.Slice(file, Files.Inspect(file).Sha256, specs[i] with { SourceX = 0 }, o, Path.Combine(worlds, specs[i].Name + ".wld"));
                results.Add(result with { Spec = specs[i] });
            }
            WriteConfigs(output, o, 0);
            var seams = new[] { WorldIO.Compare(results[0].File.Path, results[1].File.Path, o.Overlap), WorldIO.Compare(results[1].File.Path, results[2].File.Path, o.Overlap) };
            Files.Write(Path.Combine(output, "seam-checks.json"), seams);
            if (seams.Any(s => s.Mismatches != 0 || s.LeftDigest != s.RightDigest)) throw new InvalidDataException("独立标准地图接缝不一致。");
            foreach (var result in results)
            {
                Stage?.Invoke("UTSL原生离线读图：" + result.Spec.Name, .88 + .02 * results.IndexOf(result));
                await RunWorker(Path.Combine(output, "native-load-" + result.Spec.Role), o.Width, o.Height, o.Seed, o.Difficulty, result.File.Path, ct);
                using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "native-load-" + result.Spec.Role, "native-result.json")));
                if (native.RootElement.GetProperty("spawnX").GetInt32() != result.SpawnX || native.RootElement.GetProperty("spawnY").GetInt32() != result.SpawnY)
                    throw new InvalidDataException("原生读取出生点不一致。");
            }
            ct.ThrowIfCancellationRequested(); Stage?.Invoke("渲染长图与最终验收", .97); Preview.Render(output, results.ToArray(), o);
            for (int i = 0; i < 3; i++) if (Files.Inspect(inputs[i]).Sha256 != inputArtifacts[i].Sha256) throw new IOException("原生标准地图输入被意外修改。");
            var record = new GenerationRecord(o, inputArtifacts[1], 0, results.ToArray(), seams, "LOCAL_STANDARD_WORLDS_SEA_AND_NATIVE_LOAD_VERIFIED_NOT_DEPLOYED", "UNVERIFIED", DateTimeOffset.Now);
            Files.Write(Path.Combine(output, "generation-result.json"), record); VerifyExisting(output, log);
            Files.Write(Path.Combine(output, "artifact-manifest.json"), Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith("INCOMPLETE.txt")).Order().Select(p => new { path = Path.GetRelativePath(output, p), file = Files.Inspect(p) }).ToArray());
            File.Delete(Path.Combine(output, "INCOMPLETE.txt")); Files.Write(Path.Combine(output, "COMPLETE.json"), new { record.Status, record.PlayerE2E, result = Files.Inspect(Path.Combine(output, "generation-result.json")), productionTouched = false });
            log("完成：三张独立原生标准地图、同海平面接缝与长图；未部署，真人换乘未验证。"); Stage?.Invoke("本地生成完成（未部署）", 1);
        }
        catch (Exception ex) { if (owns) Files.Write(Path.Combine(output, "FAILED.json"), new { status = "FAILED_NOT_FOR_DEPLOYMENT", error = ex.ToString(), at = DateTimeOffset.Now }); throw; }
        finally { Gate.Release(); }
    }
}
