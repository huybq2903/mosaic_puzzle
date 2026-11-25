/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-12
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Falcon.Helpers.Devkit;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    public class LogSendService : MySingleton<LogSendService>
    {
        private const string SINGLE_URL = "https://dwhapi-v2.data4game.com/event-log-v2";
        private const string BATCH_URL = "https://dwhapi-v2.data4game.com/batch/event-log-v2";
        private readonly AnalyticConfigService _configService;

        public LogSendService(AnalyticConfigService configService)
        {
            _configService = configService;
        }

        public async Task Send(DataWrapper wrapper) 
        {
            if(_configService.ShouldNotSendLogToServer) return;

            // Per DWH-EventLog-API-Spec §6.1: endpoint đơn trả plain text, success = đúng chuỗi này (server có thể kèm whitespace cuối).
            var response = await (await new PostRequest(SINGLE_URL).SetJsonBody(wrapper).Execute()).SuccessStrBody();
            if (string.Equals(response?.TrimEnd(), "Request processed successfully.", StringComparison.Ordinal))
            {
                AnalyticLogger.Instance.Info(wrapper.@event + " has been sent successfully");
            }
            else
                Debug.LogError(wrapper.@event +
                               " has been sent failed with the response of: " + response);

            if (_configService.Testing)
            {
                (await new PostRequest(_configService.TestingSingleUrl).SetJsonBody(wrapper).Execute()).Close();
            }
        }

        /// <summary>
        /// Đường gửi khẩn (hợp đồng đợt 3 S2 §0.4): <b>cùng endpoint, cùng envelope</b> với <see cref="Send"/> /
        /// <see cref="SendBatch"/> — một dòng gửi endpoint đơn (thân là wrapper), nhiều dòng gửi endpoint batch (thân là
        /// mảng wrapper đã chuỗi hoá). Khác: timeout ngắn, không ghi log, không gửi bản sao testing, không đọc cấu hình
        /// (caller đã chụp cờ tắt gửi). Chạy được ngoài main thread.
        /// </summary>
        /// <returns>true nếu server nhận hết.</returns>
        public static async Task<bool> TrySendQuiet(IReadOnlyList<DataWrapper> wrappers, TimeSpan timeout)
        {
            if (wrappers == null || wrappers.Count == 0) return true;
            try
            {
                if (wrappers.Count == 1)
                {
                    var request = new PostRequest(SINGLE_URL) { Timeout = timeout };
                    var body = await (await request.SetJsonBody(wrappers[0]).Execute()).SuccessStrBody();
                    return string.Equals(body?.TrimEnd(), "Request processed successfully.", StringComparison.Ordinal);
                }

                var batch = new PostRequest(BATCH_URL) { Timeout = timeout };
                var response = await (await batch.SetJsonBody(wrappers.Select(wrapper => wrapper.ToJson())).Execute())
                    .SuccessObj<BatchProcessResponse>();
                return response != null && (response.errors == null || response.errors.Count == 0);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task SendBatch(ICollection<DataWrapper> wrappers)
        {
            if(!wrappers.Any() || _configService.ShouldNotSendLogToServer) return;
            if (wrappers.Count == 1)
            {
                await Send(wrappers.First());
                return;
            }
            var batchResponse = await (await new PostRequest(BATCH_URL).SetJsonBody(wrappers.Select(wrapper => wrapper.ToJson())).Execute()).SuccessObj<BatchProcessResponse>();
            if (batchResponse.errors.Count > 0)
            {
                foreach (var messageProcessErrorInfo in batchResponse.errors)
                {
                    Debug.LogError("Message has been sent failed with the response of: " +
                                   messageProcessErrorInfo.exception);
                }
            }
            else
            {
                AnalyticLogger.Instance.Info(wrappers.Count + " logs has been sent successfully");
            }
            
            if (_configService.Testing)
            {
                (await new PostRequest(_configService.TestingBatchUrl).SetJsonBody(wrappers).Execute()).Close();
            }
        }
    }
}