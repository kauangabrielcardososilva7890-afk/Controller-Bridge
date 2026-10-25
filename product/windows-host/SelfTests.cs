using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Buffers.Binary;

namespace ControllerBridge.Host;

public static class SelfTests
{
    private static int _assertions;
    public static async Task<int> RunAsync()
    {
        Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "Windows entry point must stay on an STA thread for clipboard/OLE APIs.");
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var temporary = Path.Combine(temporaryRoot, "controller-bridge-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporary);
            TestStateAndProjection();
            TestLanguageSelection();
            TestApplicationPaths();
            TestStartupMode();
            TestUsbReverseDeviceParsing();
            TestMotionParsingAndReport();
            TestMotionProfilesAndSwitchRollback(Path.Combine(temporary, "motion-profiles"));
            TestSessionLifecycle(Path.Combine(temporary, "profiles"));
            TestOutputLimit();
            TestXInputSlots();
            TestDistinctProfiles(Path.Combine(temporary, "distinct-profiles"));
            await TestLatestQueueAsync(Path.Combine(temporary, "queue-profiles"));
            await TestPingCoalescingAndQueueAgeAsync(Path.Combine(temporary, "aged-profiles"));
            await TestTcpAsync(Path.Combine(temporary, "tcp-profiles"));
            await TestLineLimitAsync();
            _assertions += await WifiSelfTests.RunAsync(Path.Combine(temporary, "wifi"));
            Console.WriteLine($"PASS: {_assertions} verificações de estado, perfis, saídas, enquadramento TCP e liberação de comandos.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL após {_assertions} verificações: {error}");
            return 1;
        }
        finally
        {
            var resolved = Path.GetFullPath(temporary);
            if (resolved.StartsWith(Path.Combine(temporaryRoot, "controller-bridge-tests-"), StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved))
                Directory.Delete(resolved, true);
        }
    }

    private static void TestApplicationPaths()
    {
        const string executable = @"C:\apps\ControllerBridge\ControllerBridge.exe";
        const string extractedBase = @"C:\Users\User\AppData\Local\Temp\.net\ControllerBridge\bundle";
        Check(ApplicationPaths.ResolveBaseDirectory(executable, extractedBase) == @"C:\apps\ControllerBridge", "Packaged content must resolve beside the executable, not in the bundle extraction cache.");
        Check(ApplicationPaths.ResolveBaseDirectory(null, extractedBase) == extractedBase, "Framework-dependent development runs must retain their runtime base directory.");
    }

    private static void TestStateAndProjection()
    {
        var source = GamepadState.Neutral();
        var axes = source.Axes.ToDictionary(); axes["lx"] = 9; axes["lt"] = -1;
        var state = ParseState(new(source.Buttons, axes));
        Check(state.Axis("lx") == 1 && state.Axis("lt") == 0, "Estados fora do intervalo precisam ser limitados.");
        Check(AxisProjection.XboxStick(-1) == short.MinValue && AxisProjection.XboxStick(1) == short.MaxValue, "Intervalo Xbox inteiro.");
        Check(AxisProjection.Ds4Stick(-1) == 0 && AxisProjection.Ds4Stick(0) == 128 && AxisProjection.Ds4Stick(1) == 255, "Centro e extremos DS4.");
        Check(AxisProjection.Trigger(0) == 0 && AxisProjection.Trigger(1) == 255, "Intervalo de gatilhos.");
        var buttons = source.Buttons.ToDictionary(); buttons["dpad_up"] = true; buttons["dpad_right"] = true;
        Check(AxisProjection.Dpad(new(buttons, source.Axes)) == "Northeast", "Diagonal do direcional.");
        buttons["dpad_down"] = true; buttons["dpad_left"] = true;
        Check(AxisProjection.Dpad(new(buttons, source.Axes)) == "None", "Direções opostas se anulam.");
        using var incomplete = JsonDocument.Parse("{\"buttons\":{},\"axes\":{}}");
        Expect<ProtocolException>(() => GamepadState.Parse(incomplete.RootElement), "Snapshot incompleto precisa ser rejeitado.");
        using var invalidAxis = JsonDocument.Parse(JsonSerializer.Serialize(new { buttons = source.Buttons, axes = source.Axes }).Replace("\"lx\":0", "\"lx\":\"wrong\""));
        Expect<ProtocolException>(() => GamepadState.Parse(invalidAxis.RootElement), "Eixo com tipo inválido não pode encerrar a sessão.");
        var digitalButtons = source.Buttons.ToDictionary(); digitalButtons["l2"] = true;
        var analogAxes = source.Axes.ToDictionary(); analogAxes["lt"] = .6f;
        Check(AxisProjection.TriggerValue(new(digitalButtons, analogAxes), "lt", "l2") == .6f, "Botão L2 não deve elevar um gatilho analógico ao máximo.");
    }
    private static void TestLanguageSelection()
    {
        var brazil = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        var english = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        Check(HostLanguage.Resolve(null, "pt-BR", english) == "pt", "Pacote PT-BR inicia em português mesmo com Windows em inglês.");
        Check(HostLanguage.Resolve(null, "en", brazil) == "en", "Pacote EN inicia em inglês mesmo com Windows em português.");
        Check(HostLanguage.Resolve("en", "pt-BR", brazil) == "en", "Override --lang en prevalece no pacote PT-BR.");
        Check(HostLanguage.Resolve("pt", "en", english) == "pt", "Override --lang pt prevalece no pacote EN.");
        Check(HostLanguage.Resolve(null, "auto", brazil) == "pt", "Build padrão acompanha Windows português.");
        Check(HostLanguage.Resolve(null, "auto", english) == "en", "Build padrão acompanha Windows inglês.");
        Expect<ArgumentException>(() => HostLanguage.Resolve("invalid", "en", english), "Idioma inválido é recusado.");
    }

    private static void TestStartupMode()
    {
        Check(StartupMode.UseWifi([], false, false), "Source startup must default to Wi-Fi without prompting.");
        Check(StartupMode.UseWifi(["--wifi", "--lang", "pt"], false, true), "Explicit Wi-Fi mode must stay enabled.");
        Check(!StartupMode.UseWifi(["--usb", "--lang", "pt"], false, true), "USB option must select USB explicitly.");
        Check(!StartupMode.UseWifi(["--diagnostic"], false, true), "Diagnostics must not prompt for a transport.");
        var originalInput = Console.In;
        try
        {
            Console.SetIn(new StringReader("\n"));
            Check(!StartupMode.UseWifi([], true, true), "Packaged startup must prepare USB when Enter is pressed.");
            Console.SetIn(new StringReader("2\n"));
            Check(StartupMode.UseWifi([], true, true), "Packaged startup must allow choosing Wi-Fi.");
            Console.SetIn(new StringReader("invalid\n1\n"));
            Check(!StartupMode.UseWifi([], false, true), "Invalid selection must be retried before choosing USB.");
        }
        finally { Console.SetIn(originalInput); }
        Expect<ArgumentException>(() => StartupMode.UseWifi(["--usb", "--wifi"], false, true), "Conflicting USB and Wi-Fi modes must be rejected.");
    }

    private static void TestUsbReverseDeviceParsing()
    {
        var devices = UsbReverseMonitor.ParseAuthorizedDevices(["List of devices attached", "phone1 unauthorized usb:1-1", "phone2 device product:x model:POCO", "emulator-5554 offline", "* daemon started successfully"]);
        Check(devices.Count == 1 && devices[0] == "phone2", "USB monitor must reverse only authorized ADB devices.");
        Check(UsbReverseMonitor.ParseAuthorizedDevices(["List of devices attached", ""]).Count == 0, "USB monitor must wait cleanly when no phone is connected.");
        var multiple = new[] { "phone1", "phone2" };
        Check(UsbReverseMonitor.SelectSessionDevice(multiple, "phone2") == "phone2", "USB monitor must honor the phone selected during setup.");
        Check(UsbReverseMonitor.RefreshSessionDevice("phone2", multiple, "phone2") == "phone2", "USB monitor must retain the current authorized phone.");
        Check(UsbReverseMonitor.RefreshSessionDevice("phone2", ["phone1"], "phone2") == "phone1", "USB monitor must switch to an available phone after the current phone disconnects.");
        Check(UsbReverseMonitor.RefreshSessionDevice("phone2", [], "phone2") == null, "USB monitor must wait when no authorized phone is available.");
    }

    private static void TestMotionParsingAndReport()
    {
        var neutral = GamepadState.Neutral();
        Check(ParseState(neutral).Motion == null, "Cliente antigo sem motion continua aceito.");
        using var motionDoc = JsonDocument.Parse(StateWithMotion("1", 1, 16000, [Math.PI * 3, 0, 0], [0, 0, 9.80665]));
        var motionState = GamepadState.Parse(motionDoc.RootElement);
        Check(motionState.Motion?.TimestampNanoseconds == 16000 && motionState.Motion.GyroRadiansPerSecond.X == Math.PI * 3, "Movimento SI e timestamp são preservados.");
        foreach (var invalid in new[]
        {
            "{\"gyro_rad_s\":[1,2],\"accel_m_s2\":[0,0,0],\"timestamp_ns\":1}",
            "{\"gyro_rad_s\":[0,0,1e999],\"accel_m_s2\":[0,0,0],\"timestamp_ns\":1}",
            "{\"gyro_rad_s\":[0,0,0],\"accel_m_s2\":[0,0,0],\"timestamp_ns\":-1}"
        })
        {
            using var doc = JsonDocument.Parse(State("1", 1, false)[..^1] + ",\"motion\":" + invalid + "}");
            Expect<ProtocolException>(() => GamepadState.Parse(doc.RootElement), "Movimento incompleto, não finito ou com timestamp negativo é rejeitado.");
        }
        var report = Ds4ReportEncoder.Encode(neutral, 0, 16000);
        Check(report.Length == 63 && report[0] == 128 && report[1] == 128 && BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(4)) == 8, "Relatório DS4 completo mantém sticks centrados e D-pad neutro.");
        Check(BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(9)) == 3, "Timestamp DS4 usa ticks de 16/3 microssegundos.");
        Check(BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(12)) == 1 && BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(20)) == 8032, "Repouso gyro-zero/Y-up usa calibração fixa ViGEm inversa.");
        Check(report[34] == 128 && report[38] == 128 && report[43] == 128 && report[47] == 128, "Relatório não cria toques fantasma.");
        var movingReport = Ds4ReportEncoder.Encode(motionState);
        Check(BinaryPrimitives.ReadInt16LittleEndian(movingReport.AsSpan(12)) == 8839 && BinaryPrimitives.ReadInt16LittleEndian(movingReport.AsSpan(22)) == 7482, "540 graus/s e +1g geram os pontos de calibração esperados nos offsets corretos.");
        var extreme = neutral with { Motion = new(new(1e100, -1e100, 0), new(0, 1e100, -1e100), long.MaxValue) };
        var extremes = Ds4ReportEncoder.Encode(extreme);
        Check(BinaryPrimitives.ReadInt16LittleEndian(extremes.AsSpan(12)) == short.MaxValue && BinaryPrimitives.ReadInt16LittleEndian(extremes.AsSpan(14)) == short.MinValue && BinaryPrimitives.ReadInt16LittleEndian(extremes.AsSpan(22)) == short.MinValue, "Sensores saturam em Int16 sem overflow.");
        var allButtons = neutral.Buttons.ToDictionary(); foreach (var name in GamepadState.ButtonNames) allButtons[name] = true;
        var axes = neutral.Axes.ToDictionary(); axes["lx"] = -1; axes["ly"] = 1; axes["lt"] = .25f; axes["rt"] = .75f;
        var packed = Ds4ReportEncoder.Encode(new(allButtons, axes), 2);
        Check(BinaryPrimitives.ReadUInt16LittleEndian(packed.AsSpan(4)) == 0xFFF8 && packed[6] == 9 && packed[0] == 0 && packed[1] == 255 && packed[7] == 64 && packed[8] == 191, "Botões, PS, contador, sticks e gatilhos são combinados no mesmo relatório estendido.");
    }

    private static void TestMotionProfilesAndSwitchRollback(string directory)
    {
        var factory = new RecordingFactory();
        using var pool = new OutputPool(factory);
        var store = new ProfileStore(directory);
        using var session = new ControllerSession(pool, store, _ => { });
        session.ProcessLine(Hello());
        var initial = JsonSerializer.SerializeToElement(session.ProcessLine(Attach("1")));
        Check(!initial.GetProperty("gyro_enabled").GetBoolean(), "Perfil antigo/padrão inicia gyro desativado.");
        session.ProcessLine(StateWithMotion("1", 1, 1000));
        Check(factory.Outputs[^1].Last.Motion == null, "Movimento é removido quando desativado.");
        var oldWasNeutral = false;
        factory.BeforeCreate = _ => oldWasNeutral = factory.Outputs[^1].Last.IsNeutral;
        var enabled = JsonSerializer.SerializeToElement(session.ProcessLine(MotionProfileUpdate(2, "ds4", true)));
        Check(oldWasNeutral && factory.Outputs[0].Disposed && enabled.GetProperty("gyro_enabled").GetBoolean(), "Saída antiga é neutralizada antes de criar DS4 e removida após sucesso.");
        Check(store.Load("profile-1").GyroEnabled, "gyro_enabled é persistido por perfil.");
        session.ProcessLine(StateWithMotion("1", 2, 1000));
        Check(factory.Outputs[^1].Last.Motion != null, "DS4 habilitado recebe movimento.");
        Check(Code(session.ProcessLine(StateWithMotion("1", 3, 999))) == "invalid_motion", "Timestamp de sensor regressivo é rejeitado.");
        session.ProcessLine(MotionProfileUpdate(3, "xbox360", true));
        session.ProcessLine(StateWithMotion("1", 4, 2000));
        Check(factory.Outputs[^1].Kind == OutputKind.Xbox360 && factory.Outputs[^1].Last.Motion == null, "DS4→Xbox recria dispositivo e não envia movimento a XInput.");
        session.ProcessLine(MotionProfileUpdate(4, "ds4", false));
        session.ProcessLine(StateWithMotion("1", 5, 3000));
        Check(factory.Outputs[^1].Kind == OutputKind.Ds4 && factory.Outputs[^1].Last.Motion == null && !store.Load("profile-1").GyroEnabled, "Xbox→DS4 funciona e desabilitar gyro limpa o movimento.");

        var legacyId = "legacy-profile";
        var legacyPath = Path.Combine(directory, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(legacyId))) + ".json");
        File.WriteAllText(legacyPath, JsonSerializer.Serialize(new { profileId = legacyId, descriptor = "legacy", revision = 2, output = "ds4", mapping = new { } }));
        Check(!store.Load(legacyId).GyroEnabled, "Perfil salvo antes de gyro_enabled continua compatível.");

        var failedFactory = new RecordingFactory();
        using var failedPool = new OutputPool(failedFactory);
        var failedStore = new ProfileStore(Path.Combine(directory, "failure"));
        using var failedSession = new ControllerSession(failedPool, failedStore, _ => { });
        failedSession.ProcessLine(Hello()); failedSession.ProcessLine(Attach("1")); failedSession.ProcessLine(State("1", 1, true));
        var neutralBeforeFailure = false;
        failedFactory.BeforeCreate = _ => { neutralBeforeFailure = failedFactory.Outputs[0].Last.IsNeutral; throw new InvalidOperationException("Simulated allocation failure"); };
        Check(Code(failedSession.ProcessLine(MotionProfileUpdate(2, "ds4", true))) == "output_unavailable" && neutralBeforeFailure, "Falha de substituição ocorre com saída antiga já neutralizada.");
        Check(!failedFactory.Outputs[0].Disposed && failedStore.Load("profile-1").Output == "xbox360", "Falha de criação preserva dispositivo e perfil anteriores.");
        failedSession.ProcessLine(State("1", 2, true));
        Check(failedFactory.Outputs[0].Last.Button("south"), "Dispositivo anterior continua utilizável após rollback.");

        var blockedDirectory = Path.Combine(directory, "blocked-store"); File.WriteAllText(blockedDirectory, "test");
        var saveFactory = new RecordingFactory(); using var savePool = new OutputPool(saveFactory);
        using var saveSession = new ControllerSession(savePool, new ProfileStore(blockedDirectory), _ => { });
        saveSession.ProcessLine(Hello()); saveSession.ProcessLine(Attach("1")); saveSession.ProcessLine(State("1", 1, true));
        Check(Code(saveSession.ProcessLine(MotionProfileUpdate(2, "ds4", true))) == "host_error" && saveFactory.Outputs[1].Disposed && !saveFactory.Outputs[0].Disposed && saveFactory.Outputs[0].Last.IsNeutral, "Falha ao salvar remove substituto e mantém saída anterior neutra.");
    }

    private static void TestSessionLifecycle(string directory)
    {
        var factory = new RecordingFactory();
        using var pool = new OutputPool(factory);
        var store = new ProfileStore(directory);
        var now = DateTimeOffset.UtcNow;
        using var session = new ControllerSession(pool, store, _ => { }, () => now);
        Check(Code(session.ProcessLine(Attach("1"))) == "hello_required", "Handshake obrigatório.");
        Check(Type(session.ProcessLine(Hello())) == "welcome", "Handshake retorna welcome.");
        Check(Code(session.ProcessLine("{\"v\":\"bad\",\"type\":\"ping\",\"seq\":1}")) == "unsupported_version", "Versão com tipo inválido é rejeitada sem falha do receptor.");
        Check(Type(session.ProcessLine(Attach("1"))) == "profile", "Attach retorna perfil.");
        session.ProcessLine(Attach("2"));
        session.ProcessLine(State("1", 1, true));
        session.ProcessLine(State("2", 1, false));
        Check(factory.Outputs[0].Last.Button("south") && !factory.Outputs[1].Last.Button("south"), "Controles independentes.");
        Check(Code(session.ProcessLine(State("1", 1, true))) == "stale_sequence", "Sequência duplicada rejeitada.");
        Check(factory.Outputs[0].Last.IsNeutral, "Rejeição libera botões.");
        session.ProcessLine(State("1", 2, true));
        now += TimeSpan.FromMilliseconds(1001);
        session.NeutralizeStale();
        Check(factory.Outputs.All(x => x.Last.IsNeutral), "Timeout neutraliza todos os controles sem estado recente.");
        now += TimeSpan.FromMilliseconds(500);
        session.ProcessLine(State("1", 3, true));
        now += TimeSpan.FromMilliseconds(750);
        session.ProcessLine(State("1", 4, true));
        now += TimeSpan.FromMilliseconds(750);
        session.NeutralizeStale();
        Check(factory.Outputs[0].Last.Button("south"), "Snapshots idênticos com sequência crescente mantêm botões pressionados.");
        Check(Code(session.ProcessLine("{invalid")) == "invalid_json" && factory.Outputs.All(x => x.Last.IsNeutral), "JSON inválido neutraliza a sessão.");
        var profileReply = session.ProcessLine("{\"v\":1,\"type\":\"profile_update\",\"controller_id\":\"1\",\"profile_id\":\"profile-1\",\"revision\":2,\"output\":\"ds4\",\"mapping\":{\"south\":{\"key\":97},\"ly\":{\"invert\":true}}}");
        Check(Type(profileReply) == "profile_saved", "Perfil salvo confirmado.");
        Check(factory.Outputs[0].Disposed && factory.Outputs[^1].Kind == OutputKind.Ds4, "Trocar saída libera o dispositivo anterior.");
        var loaded = store.Load("profile-1");
        Check(loaded.Output == "ds4" && loaded.Revision == 2 && loaded.Mapping.GetProperty("south").GetProperty("key").GetInt32() == 97, "Perfil persistido com mapeamentos.");
        Check(Code(session.ProcessLine("{\"v\":1,\"type\":\"profile_update\",\"controller_id\":\"1\",\"profile_id\":\"profile-1\",\"revision\":2,\"output\":\"ds4\",\"mapping\":{}}")) == "stale_revision", "Revisão antiga rejeitada.");
        session.ProcessLine("{\"v\":1,\"type\":\"detach\",\"controller_id\":\"1\"}");
        Check(factory.Outputs[^1].Disposed, "Detach libera a saída.");
        session.Dispose();
        Check(factory.Outputs.All(x => x.Disposed), "Desconexão libera todas as saídas da sessão.");
        using var restored = new ControllerSession(pool, store, _ => { });
        restored.ProcessLine(Hello());
        var reply = JsonSerializer.SerializeToElement(restored.ProcessLine(Attach("1")));
        Check(reply.GetProperty("output").GetString() == "ds4" && reply.GetProperty("revision").GetInt32() == 2 && reply.GetProperty("profile_id").GetString() == "profile-1", "Reconexão recupera o perfil pelo ID persistente.");
    }

    private static void TestDistinctProfiles(string directory)
    {
        using var pool = new OutputPool(new RecordingFactory());
        var store = new ProfileStore(directory);
        using var session = new ControllerSession(pool, store, _ => { });
        session.ProcessLine(Hello());
        session.ProcessLine(Attach("1", "identical-descriptor", "first-profile"));
        session.ProcessLine(Attach("2", "identical-descriptor", "second-profile"));
        Check(Type(session.ProcessLine(ProfileUpdate("1", "first-profile", 2, 10))) == "profile_saved", "Primeiro perfil independente salvo.");
        Check(Type(session.ProcessLine(ProfileUpdate("2", "second-profile", 2, 20))) == "profile_saved", "Segundo perfil independente salvo.");
        Check(store.Load("first-profile").Mapping.GetProperty("key").GetInt32() == 10 && store.Load("second-profile").Mapping.GetProperty("key").GetInt32() == 20, "Descritores iguais não compartilham mapeamento quando os IDs diferem.");
        Check(Code(session.ProcessLine(ProfileUpdate("1", "second-profile", 3, 30))) == "profile_id_mismatch", "Atualização não pode trocar o ID do perfil anexado.");
        Check(store.Load("second-profile").Revision == 2, "Perfil de outro controle não foi alterado.");
    }

    private static void TestXInputSlots()
    {
        var occupiedFactory = new RecordingFactory();
        using var occupied = new OutputPool(occupiedFactory, new FakeSlotProbe(15, false));
        Check(CreateError(occupied) == "output_limit" && occupiedFactory.Outputs.Count == 0, "Quatro slots de sistema ocupados impedem criação, incluindo controles nativos.");
        var invisibleFactory = new RecordingFactory();
        using var invisible = new OutputPool(invisibleFactory, new FakeSlotProbe(1, false));
        Check(CreateError(invisible) == "output_unavailable" && invisibleFactory.Outputs.Single().Disposed, "Saída sem novo slot XInput é neutralizada e removida.");
        using var visible = new OutputPool(new RecordingFactory(), new FakeSlotProbe(1, true));
        using var output = visible.Create("visible", OutputKind.Xbox360);
        Check(output != null, "Novo slot XInput confirmado permite saída.");
    }
    private static string CreateError(OutputPool pool)
    {
        try { using var unexpected = pool.Create("test", OutputKind.Xbox360); return "none"; }
        catch (ProtocolException error) { return error.Code; }
    }

    private static async Task TestLatestQueueAsync(string directory)
    {
        using var queue = new LatestMessageQueue();
        for (var sequence = 1; sequence <= 1000; sequence++)
            await queue.EnqueueAsync(State("1", sequence, sequence < 1000), CancellationToken.None);
        Check(queue.PendingBatchCount == 1, "Mil snapshots pendentes ocupam somente um lote limitado.");
        await queue.EnqueueAsync(ProfileUpdate("1", "profile-1", 2, 7), CancellationToken.None);
        await queue.EnqueueAsync(State("1", 1001, true), CancellationToken.None);
        queue.Complete();
        var factory = new RecordingFactory();
        using var pool = new OutputPool(factory);
        using var session = new ControllerSession(pool, new ProfileStore(directory), _ => { });
        session.ProcessLine(Hello()); session.ProcessLine(Attach("1"));
        var batch = (await queue.DequeueAsync(CancellationToken.None))!;
        Check(batch.Length == 1 && JsonDocument.Parse(batch[0].Line).RootElement.GetProperty("seq").GetInt64() == 1000, "Somente o snapshot mais recente permanece na fila.");
        session.ProcessLine(batch[0].Line, batch[0].ReceivedAt);
        Check(factory.Outputs.Single().ApplyCount == 1 && !factory.Outputs.Single().Last.Button("south"), "Estados antigos não são reproduzidos no adaptador.");
        var barrier = (await queue.DequeueAsync(CancellationToken.None))!;
        Check(Type(session.ProcessLine(barrier.Single().Line, barrier.Single().ReceivedAt)) == "profile_saved", "Mensagem de configuração permanece entre os lotes de estado.");
        var afterBarrier = (await queue.DequeueAsync(CancellationToken.None))!;
        session.ProcessLine(afterBarrier.Single().Line, afterBarrier.Single().ReceivedAt);
        Check(factory.Outputs.Single().Last.Button("south"), "Estado depois da barreira só é aplicado depois dela.");
    }

    private static async Task TestPingCoalescingAndQueueAgeAsync(string directory)
    {
        var now = DateTimeOffset.UtcNow;
        using var queue = new LatestMessageQueue(() => now);
        await queue.EnqueueAsync(Hello(), CancellationToken.None);
        await queue.EnqueueAsync(State("1", 1, true), CancellationToken.None);
        await queue.EnqueueAsync("{\"v\":1,\"type\":\"ping\",\"seq\":9}", CancellationToken.None);
        await queue.EnqueueAsync(State("1", 2, false), CancellationToken.None);
        Check(queue.PendingBatchCount == 2, "Hello permanece barreira; ping não separa snapshots pendentes.");
        var factory = new RecordingFactory();
        using var pool = new OutputPool(factory);
        using var session = new ControllerSession(pool, new ProfileStore(directory), _ => { }, () => now);
        var hello = (await queue.DequeueAsync(CancellationToken.None))!.Single();
        Check(Type(session.ProcessLine(hello.Line, hello.ReceivedAt)) == "welcome", "Hello é processado antes do lote de liveness/estados.");
        session.ProcessLine(Attach("1"));
        var batch = (await queue.DequeueAsync(CancellationToken.None))!;
        var replies = batch.Select(message => session.ProcessLine(message.Line, message.ReceivedAt)).Where(reply => reply != null).ToArray();
        Check(factory.Outputs.Single().ApplyCount == 1 && !factory.Outputs.Single().Last.Button("south"), "state1 pressionado → ping → state2 solto aplica somente state2.");
        Check(replies.Length == 1 && Type(replies[0]) == "pong" && JsonSerializer.SerializeToElement(replies[0]).GetProperty("seq").GetInt64() == 9, "Ping coalescido preserva pong com a sequência correta.");

        session.ProcessLine(State("1", 3, true));
        await queue.EnqueueAsync(State("1", 4, true), CancellationToken.None);
        now += TimeSpan.FromMilliseconds(1100);
        var aged = (await queue.DequeueAsync(CancellationToken.None))!.Single();
        session.ProcessLine(aged.Line, aged.ReceivedAt);
        Check(factory.Outputs.Single().ApplyCount == 2, "Snapshot enfileirado há mais de um segundo não chega à saída.");
        Check(factory.Outputs.Single().Last.IsNeutral, "Estado antigo não renova prazo; watchdog libera o comando pressionado anterior.");

        await queue.EnqueueAsync(State("1", 5, true), CancellationToken.None);
        now += TimeSpan.FromMilliseconds(600);
        var delayed = (await queue.DequeueAsync(CancellationToken.None))!.Single();
        session.ProcessLine(delayed.Line, delayed.ReceivedAt);
        Check(factory.Outputs.Single().Last.Button("south"), "Snapshot com menos de um segundo ainda é aplicado.");
        now += TimeSpan.FromMilliseconds(500);
        session.NeutralizeStale();
        Check(factory.Outputs.Single().Last.IsNeutral, "Watchdog conta o prazo desde a recepção, não desde a aplicação atrasada.");
    }

    private static void TestOutputLimit()
    {
        using var pool = new OutputPool(new RecordingFactory());
        var outputs = Enumerable.Range(0, 4).Select(i => pool.Create(i.ToString(), OutputKind.Xbox360)).ToList();
        Expect<ProtocolException>(() => pool.Create("fifth", OutputKind.Xbox360), "Quinta saída Xbox rejeitada.");
        outputs[0].Dispose();
        using var replacement = pool.Create("replacement", OutputKind.Xbox360);
        Check(replacement != null, "Slot Xbox liberado pode ser reutilizado.");
        foreach (var output in outputs) output.Dispose();
    }

    private static async Task TestTcpAsync(string directory)
    {
        var factory = new RecordingFactory();
        using var pool = new OutputPool(factory);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new TcpClient();
        var accept = listener.AcceptTcpClientAsync(cancellation.Token);
        await client.ConnectAsync(IPAddress.Loopback, port, cancellation.Token);
        var accepted = await accept;
        listener.Stop();
        var server = new HostServer(pool, new ProfileStore(directory), _ => { }).ServeClientAsync(accepted, cancellation.Token);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        var hello = Encoding.UTF8.GetBytes(Hello() + "\n");
        await stream.WriteAsync(hello.AsMemory(0, 7), cancellation.Token);
        await stream.WriteAsync(hello.AsMemory(7), cancellation.Token);
        Check(TypeJson(await reader.ReadLineAsync(cancellation.Token)) == "welcome", "TCP aceita JSON dividido entre pacotes.");
        await stream.WriteAsync(Encoding.UTF8.GetBytes(Attach("1") + "\n" + State("1", 1, true) + "\n{\"v\":1,\"type\":\"ping\",\"seq\":4}\n"), cancellation.Token);
        Check(TypeJson(await reader.ReadLineAsync(cancellation.Token)) == "profile", "TCP attach retorna perfil.");
        Check(TypeJson(await reader.ReadLineAsync(cancellation.Token)) == "pong", "TCP ping retorna pong.");
        Check(factory.Outputs.Single().Last.Button("south"), "TCP state chega ao adaptador.");
        client.Dispose();
        await server.WaitAsync(cancellation.Token);
        Check(factory.Outputs.Single().Disposed && factory.Outputs.Single().Last.IsNeutral, "Fechar TCP libera o controle.");
    }

    private static async Task TestLineLimitAsync()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 65537) + "\n"));
        using var reader = new StreamReader(stream);
        try { await new BoundedLineReader(reader, 65536).ReadLineAsync(CancellationToken.None); throw new Exception("Linha sem limite foi aceita."); }
        catch (InvalidDataException) { _assertions++; }
    }

    private static string Hello() => "{\"v\":1,\"type\":\"hello\",\"client_id\":\"test\",\"app_version\":\"0.1.0\"}";
    private static string Attach(string id, string? descriptor = null, string? profileId = null) => JsonSerializer.Serialize(new { v = 1, type = "attach", controller_id = id, profile_id = profileId ?? "profile-" + id, descriptor = descriptor ?? "descriptor-" + id, name = "Test " + id });
    private static string ProfileUpdate(string id, string profileId, int revision, int key) => JsonSerializer.Serialize(new { v = 1, type = "profile_update", controller_id = id, profile_id = profileId, revision, output = "xbox360", mapping = new { key } });
    private static string MotionProfileUpdate(int revision, string output, bool gyro) => JsonSerializer.Serialize(new { v = 1, type = "profile_update", controller_id = "1", profile_id = "profile-1", revision, output, gyro_enabled = gyro, mapping = new { } });
    private static string StateWithMotion(string id, long sequence, long timestamp, double[]? gyro = null, double[]? accel = null)
    {
        var state = GamepadState.Neutral();
        return JsonSerializer.Serialize(new { v = 1, type = "state", controller_id = id, seq = sequence, buttons = state.Buttons, axes = state.Axes, motion = new { gyro_rad_s = gyro ?? [0.1, 0.2, 0.3], accel_m_s2 = accel ?? [0, 9.80665, 0], timestamp_ns = timestamp } });
    }
    private static string State(string id, long sequence, bool south)
    {
        var state = GamepadState.Neutral(); var buttons = state.Buttons.ToDictionary(); buttons["south"] = south;
        return JsonSerializer.Serialize(new { v = 1, type = "state", controller_id = id, seq = sequence, buttons, axes = state.Axes });
    }
    private static GamepadState ParseState(GamepadState state)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { buttons = state.Buttons, axes = state.Axes }));
        return GamepadState.Parse(doc.RootElement);
    }
    private static string? Type(object? reply) => JsonSerializer.SerializeToElement(reply).GetProperty("type").GetString();
    private static string? Code(object? reply) => JsonSerializer.SerializeToElement(reply).GetProperty("code").GetString();
    private static string? TypeJson(string? line) { using var doc = JsonDocument.Parse(line ?? "null"); return doc.RootElement.GetProperty("type").GetString(); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); _assertions++; }
    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { _assertions++; return; }
        throw new Exception(message);
    }
    private sealed class RecordingFactory : IOutputFactory
    {
        public List<RecordingOutput> Outputs { get; } = [];
        public Action<OutputKind>? BeforeCreate { get; set; }
        public IControllerOutput Create(string id, OutputKind kind) { BeforeCreate?.Invoke(kind); var output = new RecordingOutput(kind); Outputs.Add(output); return output; }
        public void Dispose() { }
    }
    private sealed class RecordingOutput(OutputKind kind) : IControllerOutput
    {
        public OutputKind Kind { get; } = kind;
        public GamepadState Last { get; private set; } = GamepadState.Neutral();
        public bool Disposed { get; private set; }
        public int ApplyCount { get; private set; }
        public void Apply(GamepadState state) { Last = state; ApplyCount++; }
        public void Neutralize() => Last = GamepadState.Neutral();
        public void Dispose() { Neutralize(); Disposed = true; }
    }
    private sealed class FakeSlotProbe(uint connected, bool newSlot) : IXInputSlotProbe
    {
        public uint ConnectedMask() => connected;
        public bool WaitForNewSlot(uint previousMask, TimeSpan timeout) => newSlot;
    }
}
