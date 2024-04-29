using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UPnPLibrary;
using UPnPLibrary.Description.Device;
using UPnPLibrary.Description.Service;
using UPnPLibrary.Ssdp;

namespace UPnPTool
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.Write("UPnPデバイスの検索を開始しますか？（y/n）：");
            while (true)
            {
                var key = Console.ReadKey();

                // yが入力された場合は次の処理へ進む
                if (key.Key.ToString() == "Y")
                {
                    Console.WriteLine();
                    break;
                }

                // nが入力された場合はアプリ終了
                if (key.Key.ToString() == "N")
                {
                    return;
                }

                // y/n以外が入力された場合は再入力させる
                Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
                Console.Write(' ');
                Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
            }

            // デバイス検索
            List<UPnPDevice> devices = DiscoverDevices();
            
            // デバイス0件の場合は終了
            if (devices == null || devices.Count == 0)
            {
                Console.WriteLine("UPnPデバイスが見つかりませんでした。");
                Console.ReadKey();
                return;
            }

            // UPnPサービスを使用するデバイスを選択させる
            UPnPDevice useDevice = ReadUseDevice(devices);
            if (useDevice == null)
            {
                return;
            }

            List<Service> services = useDevice.DeviceDescription.GetServiceList();
            if (services.Count == 0)
            {
                Console.WriteLine("UPnPサービスが見つかりませんでした。");
                Console.ReadKey();
                return;
            }

            Service useService = null;
            if (services.Count > 1)
            {
                // UPnPサービスが複数ある場合は使用するサービスをユーザーに選択させる
                useService = ReadUseService(services);
            }
            else
            {
                useService = services[0];
            }

            // UPnP通信クライアント初期化
            UPnPClient client = new UPnPClient(useDevice.DeviceAccess);

            // サービス詳細情報取得
            ServiceDescription serviceDescription = client.RequestServiceDescriptionAsync(useService).Result;

            // 使用するサービスアクションをユーザーに選択させる
            UPnPLibrary.Description.Service.Action useAction = ReadUseAction(serviceDescription.ActionList);

            // リクエストパラメータが必要である場合はユーザーに入力させる
            Dictionary<string, string> requestArgs = new Dictionary<string, string>();
            List<Argument> inArgs = useAction.ArgumentList.Where(x => x.Direction.ToLower() == "in").ToList();
            if (inArgs.Count > 0)
            {
                requestArgs = ReadArguments(inArgs, serviceDescription.ServiceStateTable);
            }

            // UPnPサービスアクションリクエスト実行
            UPnPActionRequestMessage request = new UPnPActionRequestMessage(useService, useAction.Name, requestArgs);
            Dictionary<string, string> response = null;
            try
            {
                response = client.RequestUPnPActionAsync(request).Result;
            }
            catch (AggregateException aggEx)
            {
                if (aggEx.InnerException is UPnPActionException e)
                {
                    Console.WriteLine("サービスアクションのリクエストに失敗しました。");
                    Console.WriteLine($"エラーコード:{e.ErrorCode}");
                    Console.WriteLine($"エラーメッセージ:{e.ErrorDescription}");

                    Console.WriteLine();
                    Console.WriteLine("終了するには何かキーを入力してください。");
                    Console.ReadKey();
                    return;
                }
            }

            // レスポンス表示
            Console.WriteLine("サービスアクションのリクエストに成功しました。");
            foreach (var value in response)
            {
                Console.WriteLine($"{value.Key}:{value.Value}");
            }

            Console.WriteLine();
            Console.WriteLine("終了するには何かキーを入力してください。");
            Console.ReadKey();
        }

        /// <summary>
        /// UPnPデバイス検索
        /// </summary>
        /// <returns>発見したデバイスリスト</returns>
        private static List<UPnPDevice> DiscoverDevices()
        {
            // 戻り値
            List<UPnPDevice> devices = new List<UPnPDevice>();

            // コンソール処理終了待機用オブジェクト
            AutoResetEvent wait = new AutoResetEvent(false);

            // 「検索中...」のコンソールを非同期で表示
            CancellationTokenSource cancelTokenSource = new CancellationTokenSource();
            Task.Run(async() =>
            {
                Console.Write("検索中.");
                int dotCount = 1;

                try
                {
                    while (true)
                    {
                        await Task.Delay(1000, cancelTokenSource.Token);

                        if (dotCount >= 3)
                        {
                            Console.SetCursorPosition(Console.CursorLeft - 2, Console.CursorTop);
                            Console.Write("  ");
                            Console.SetCursorPosition(Console.CursorLeft - 2, Console.CursorTop);
                            dotCount = 1;
                        }
                        else
                        {
                            Console.Write(".");
                            dotCount++;
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }

                // 検索が終わった場合は「検索中...」の状態にする
                Console.SetCursorPosition(Console.CursorLeft - dotCount, Console.CursorTop);
                Console.WriteLine("...");
                Console.WriteLine();

                // コンソール処理終了通知
                wait.Set();
            });

            // デバイス検索
            UPnPDeviceDiscover disvocer = new UPnPDeviceDiscover();
            disvocer.SearchTargets = new List<string>() { "ssdp:all" };
            List<UPnPDeviceAccess> deviceAccesses = disvocer.FindDevicesAsync().Result;

            // 各デバイス情報取得
            foreach (UPnPDeviceAccess access in deviceAccesses)
            {
                // UPnPDevice初期化
                UPnPDevice device = new UPnPDevice();
                device.DeviceAccess = access;

                // デバイス情報リクエスト
                UPnPClient client = new UPnPClient(access);
                device.DeviceDescription = client.RequestDeviceDescriptionAsync().Result;

                // リストに追加
                devices.Add(device);
            }

            // コンソール表示処理終了
            cancelTokenSource.Cancel();

            // コンソール終了処理が終わるまで待機
            wait.WaitOne();

            return devices;
        }

        /// <summary>
        /// UPnPサービスを使用するデバイスをユーザーに選択させる
        /// </summary>
        /// <param name="devices">デバイス一覧</param>
        /// <returns>選択されたデバイス</returns>
        private static UPnPDevice ReadUseDevice(List<UPnPDevice> devices)
        {
            // 戻り値
            UPnPDevice device = null;

            Console.WriteLine("以下のデバイスが見つかりました。");

            for (int i = 0; i < devices.Count; i++)
            {
                Device deviceInfo = devices[i].DeviceDescription.Device;

                // デバイスが複数見つかった場合のみ番号表示
                if (devices.Count > 1)
                {
                    Console.WriteLine($"{i + 1}.");
                }

                // 製品名表示
                if (!string.IsNullOrEmpty(deviceInfo.FriendlyName))
                {
                    Console.WriteLine($"製品名：{deviceInfo.FriendlyName}");
                }

                // IPアドレス表示
                IPAddress ipAddress = devices[i].DeviceAccess.IpAddress;
                Console.WriteLine($"IPアドレス：{ipAddress}");

                // メーカー名表示
                if (!string.IsNullOrEmpty(deviceInfo.Manufacturer))
                {
                    Console.WriteLine($"メーカー名：{deviceInfo.Manufacturer}");
                }

                // メーカーWebサイト表示
                if (!string.IsNullOrEmpty(deviceInfo.ManufacturerURL))
                {
                    Console.WriteLine($"メーカーWebサイト：{deviceInfo.ManufacturerURL}");
                }

                Console.WriteLine();
            }

            if (devices.Count > 1)
            {
                Console.Write($"UPnPサービスを使用するデバイスを選択してください（1～{devices.Count}）：");

                // 再入力用に現在のカーソル座標を保存
                int cursorLeft = Console.CursorLeft;
                int cursorTop = Console.CursorTop;

                while (true)
                {
                    // 入力待ち
                    string inputIndex = Console.ReadLine();

                    // 数値変換
                    if (int.TryParse(inputIndex, out int index))
                    {
                        // 入力された番号がリストの範囲内の場合、指定されたデバイス取り出し
                        if (index >= 1 && index <= devices.Count)
                        {
                            device = devices[index - 1];
                            break;
                        }
                    }

                    // 再入力
                    Console.SetCursorPosition(cursorLeft, cursorTop);
                    Console.Write(new string(' ', inputIndex.Length));
                    Console.SetCursorPosition(cursorLeft, cursorTop);
                }
            }
            else
            {
                Console.Write("UPnPサービスを使用しますか？（y/n）：");
                while (true)
                {
                    var key = Console.ReadKey();

                    // yが入力された場合はデバイスを返す
                    if (key.Key.ToString() == "Y")
                    {
                        Console.WriteLine();
                        device = devices[0];
                        break;
                    }

                    // nが入力された場合はnullを返す
                    if (key.Key.ToString() == "N")
                    {
                        break;
                    }

                    // y/n以外が入力された場合は再入力させる
                    Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
                    Console.Write(' ');
                    Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
                }
            }

            Console.WriteLine();
            return device;
        }

        /// <summary>
        /// 使用するUPnPサービスをユーザーに選択させる
        /// </summary>
        /// <param name="services">サービス一覧</param>
        /// <returns>選択されたサービス</returns>
        private static Service ReadUseService(List<Service> services)
        {
            // 戻り値
            Service service = null;

            Console.WriteLine("UPnPサービスが複数見つかりました。");
            for (int i = 0; i < services.Count; i++)
            {
                Console.WriteLine($"{i + 1}.{services[i].ServiceTypeName}");
            }
            Console.WriteLine();

            Console.Write($"使用するUPnPサービスを選択してください（1～{services.Count}）：");

            // 再入力用に現在のカーソル座標を保存
            int cursorLeft = Console.CursorLeft;
            int cursorTop = Console.CursorTop;

            while (true)
            {
                // 入力待ち
                string inputIndex = Console.ReadLine();

                // 数値変換
                if (int.TryParse(inputIndex, out int index))
                {
                    // 入力された番号がリストの範囲内の場合、指定されたサービス取り出し
                    if (index >= 1 && index <= services.Count)
                    {
                        service = services[index - 1];
                        break;
                    }
                }

                // 再入力
                Console.SetCursorPosition(cursorLeft, cursorTop);
                Console.Write(new string(' ', inputIndex.Length));
                Console.SetCursorPosition(cursorLeft, cursorTop);
            }

            Console.WriteLine();
            return service;
        }

        /// <summary>
        /// 使用するサービスアクションをユーザーに選択させる
        /// </summary>
        /// <param name="actions">サービスアクション一覧</param>
        /// <returns>選択されたサービスアクション</returns>
        private static UPnPLibrary.Description.Service.Action ReadUseAction(List<UPnPLibrary.Description.Service.Action> actions)
        {
            // 戻り値
            UPnPLibrary.Description.Service.Action action = null;

            Console.WriteLine("以下のサービスアクションが見つかりました。");

            for (int i = 0; i < actions.Count; i++)
            {
                Console.WriteLine($"{i + 1}.{actions[i].Name}");
            }

            Console.WriteLine();

            Console.Write($"使用するサービスアクションを選択してください（1～{actions.Count}）：");

            // 再入力用に現在のカーソル座標を保存
            int cursorLeft = Console.CursorLeft;
            int cursorTop = Console.CursorTop;

            while (true)
            {
                // 入力待ち
                string inputIndex = Console.ReadLine();

                // 数値変換
                if (int.TryParse(inputIndex, out int index))
                {
                    // 入力された番号がリストの範囲内の場合、指定されたアクション取り出し
                    if (index >= 1 && index <= actions.Count)
                    {
                        action = actions[index - 1];
                        break;
                    }
                }

                // 再入力
                Console.SetCursorPosition(cursorLeft, cursorTop);
                Console.Write(new string(' ', inputIndex.Length));
                Console.SetCursorPosition(cursorLeft, cursorTop);
            }

            Console.WriteLine();
            return action;
        }

        /// <summary>
        /// アクション引数をユーザーに入力/選択させる
        /// </summary>
        /// <param name="args">アクション引数</param>
        /// <param name="argStates">引数情報</param>
        /// <returns>入力/選択されたアクション引数</returns>
        private static Dictionary<string, string> ReadArguments(List<Argument> args, List<StateVariable> argStates)
        {
            // 戻り値
            Dictionary<string, string> map = new Dictionary<string, string>();

            Console.WriteLine("引数を入力/選択してください。");

            foreach (Argument arg in args)
            {
                // 引数情報取得
                StateVariable state = argStates.Where(x => x.Name == arg.RelatedStateVariable).FirstOrDefault();

                if (state.AllowedValueList != null && state.AllowedValueList.Count > 0)
                {
                    // 指定されたリストの中から値を選択
                    string select = ReadArgumentList(arg.Name, state.AllowedValueList);
                    map.Add(arg.Name, select);
                }
                else if (state.AllowedValueRange != null)
                {
                    // 指定された範囲の間から値を入力
                    string input = ReadArgumentRange(arg.Name, state.AllowedValueRange);
                    map.Add(arg.Name, input);
                }
                else
                {
                    // データ型を提示して自由に値を入力
                    string input = ReadArgumentFree(arg.Name, state.DataType);
                    map.Add(arg.Name, input);
                }
            }

            Console.WriteLine();
            return map;
        }

        /// <summary>
        /// 使用するアクション引数をユーザーに選択させる
        /// </summary>
        /// <param name="argName">引数名</param>
        /// <param name="valueList">引数値リスト</param>
        /// <returns>選択された引数</returns>
        private static string ReadArgumentList(string argName, List<string> valueList)
        {
            // 戻り値
            string value = string.Empty;

            Console.WriteLine(argName);

            for (int i = 0; i < valueList.Count; i++)
            {
                Console.WriteLine($"{i + 1}.{valueList[i]}");
            }
            Console.Write(":");

            while (true)
            {
                // 入力待ち
                string inputIndex = Console.ReadLine();

                // 数値変換
                if (int.TryParse(inputIndex, out int index))
                {
                    // 入力された番号がリストの範囲内の場合、指定された引数を使用
                    if (index >= 1 && index <= valueList.Count)
                    {
                        value = valueList[index - 1];
                        break;
                    }
                }

                // 再入力
                Console.SetCursorPosition(1, Console.CursorTop - 1);
                Console.Write(new string(' ', inputIndex.Length));
                Console.SetCursorPosition(1, Console.CursorTop);
            }

            return value;
        }

        /// <summary>
        /// 使用するアクション引数をユーザーに入力させる
        /// </summary>
        /// <param name="argName">引数名</param>
        /// <param name="range">引数範囲</param>
        /// <returns>入力された引数</returns>
        private static string ReadArgumentRange(string argName, AllowedValueRange range)
        {
            // 戻り値
            string value = string.Empty;

            // 引数範囲取得
            int min = int.Parse(range.Minimum);
            int max = int.Parse(range.Maximum);
            float step = 1;
            if (!string.IsNullOrEmpty(range.Step))
            {
                step = float.Parse(range.Step);
            }

            if (step == 1)
            {
                Console.Write($"{argName}（{min}～{max}）:");
            }
            else
            {
                Console.Write($"{argName}（{min}～{max},分割値:{step}）:");
            }

            // 再入力用に現在のカーソル座標を保存
            int cursorLeft = Console.CursorLeft;
            int cursorTop = Console.CursorTop;

            while (true)
            {
                // 入力待ち
                string inputStr = Console.ReadLine();

                // 数値変換
                if (float.TryParse(inputStr, out float inputNum))
                {
                    // 入力された番号がリストの範囲内の場合、指定された引数を使用
                    if (inputNum >= min && inputNum <= max && ((inputNum - min) % step) == 0)
                    {
                        value = inputStr;
                        break;
                    }
                }

                // 再入力
                Console.SetCursorPosition(cursorLeft, cursorTop);
                Console.Write(new string(' ', inputStr.Length));
                Console.SetCursorPosition(cursorLeft, cursorTop);
            }

            return value;
        }

        /// <summary>
        /// 使用するアクション引数をユーザーに入力させる
        /// </summary>
        /// <param name="argName">引数名</param>
        /// <param name="dataType">引数のデータ型</param>
        /// <returns>入力された引数</returns>
        private static string ReadArgumentFree(string argName, StateVariableDataType dataType)
        {
            if (!string.IsNullOrEmpty(dataType.Type))
            {
                Console.Write($"{argName}（データ型:{dataType.Type}）:");
            }
            else
            {
                Console.Write($"{argName}（データ型:{dataType.DataType}）:");
            }

            // 入力待ち
            return Console.ReadLine();
        }
    }
}
