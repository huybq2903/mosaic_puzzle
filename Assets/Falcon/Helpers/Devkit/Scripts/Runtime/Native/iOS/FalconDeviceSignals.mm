/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 *
 * Đọc tín hiệu máy iOS cho Falcon Devkit (DeviceSignals, hợp đồng BigData đợt 3 "(2)").
 * - Chỉ import Foundation + UIKit (Unity luôn link). Lớp của framework khác (AVFoundation, UserNotifications,
 *   AppTrackingTransparency, MetricKit) tra lúc chạy bằng NSClassFromString — game không phải thêm framework, máy iOS
 *   cũ không có lớp thì trả "không biết" (-1 / NULL).
 * - Mọi lời gọi động kiểm respondsToSelector: trước — selector lạ là văng cứng, C# không bắt được.
 * - Chuỗi trả về cấp bằng strdup (IL2CPP giải phóng bằng free).
 * - Chỉ được build khi project có define FALCON_DEVKIT_IOS_NATIVE (defineConstraints trong .meta).
 */

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <objc/message.h>
#import <objc/runtime.h>
#import <dlfcn.h>

static char *FalconDS_Copy(NSString *value)
{
    if (value == nil) return NULL;
    const char *utf8 = [value UTF8String];
    return utf8 == NULL ? NULL : strdup(utf8);
}

// Framework hệ thống chưa được link thì nạp động (nằm sẵn trong shared cache) trước khi tra lớp.
static Class FalconDS_Class(NSString *name, const char *frameworkPath)
{
    Class cls = NSClassFromString(name);
    if (cls == nil && frameworkPath != NULL)
    {
        dlopen(frameworkPath, RTLD_LAZY);
        cls = NSClassFromString(name);
    }
    return cls;
}

static id FalconDS_Send(id target, NSString *selectorName)
{
    if (target == nil) return nil;
    SEL selector = NSSelectorFromString(selectorName);
    if (![target respondsToSelector:selector]) return nil;
    return ((id (*)(id, SEL))objc_msgSend)(target, selector);
}

static long FalconDS_SendInteger(id target, NSString *selectorName, long fallback)
{
    if (target == nil) return fallback;
    SEL selector = NSSelectorFromString(selectorName);
    if (![target respondsToSelector:selector]) return fallback;
    return (long)((NSInteger (*)(id, SEL))objc_msgSend)(target, selector);
}

extern "C" {

// ---- Nhiệt độ / tiết kiệm pin ----

int FalconDS_ThermalState()
{
    NSProcessInfo *info = [NSProcessInfo processInfo];
    if (![info respondsToSelector:@selector(thermalState)]) return -1;
    return (int)info.thermalState;
}

int FalconDS_LowPowerMode()
{
    NSProcessInfo *info = [NSProcessInfo processInfo];
    if (![info respondsToSelector:@selector(isLowPowerModeEnabled)]) return -1;
    return info.isLowPowerModeEnabled ? 1 : 0;
}

// ---- Âm thanh ----

static id FalconDS_AudioSession()
{
    Class cls = FalconDS_Class(@"AVAudioSession", "/System/Library/Frameworks/AVFAudio.framework/AVFAudio");
    return FalconDS_Send(cls, @"sharedInstance");
}

float FalconDS_MediaVolume()
{
    id session = FalconDS_AudioSession();
    if (session == nil || ![session respondsToSelector:NSSelectorFromString(@"outputVolume")]) return -1;
    return ((float (*)(id, SEL))objc_msgSend)(session, NSSelectorFromString(@"outputVolume"));
}

const char *FalconDS_AudioOutputPort()
{
    id route = FalconDS_Send(FalconDS_AudioSession(), @"currentRoute");
    id outputs = FalconDS_Send(route, @"outputs");
    if (![outputs isKindOfClass:[NSArray class]] || [(NSArray *)outputs count] == 0) return NULL;
    id port = FalconDS_Send([(NSArray *)outputs firstObject], @"portType");
    return [port isKindOfClass:[NSString class]] ? FalconDS_Copy(port) : NULL;
}

// ---- Cấu hình hiển thị ----

const char *FalconDS_ContentSizeCategory()
{
    UIApplication *app = [UIApplication sharedApplication];
    if (![app respondsToSelector:@selector(preferredContentSizeCategory)]) return NULL;
    return FalconDS_Copy(app.preferredContentSizeCategory);
}

int FalconDS_BoldText()
{
    return UIAccessibilityIsBoldTextEnabled() ? 1 : 0;
}

int FalconDS_ReduceMotion()
{
    return UIAccessibilityIsReduceMotionEnabled() ? 1 : 0;
}

// ---- Quyền thông báo (đọc bất đồng bộ, giữ kết quả lần trước) ----

static volatile int gPushStatus = -1;

void FalconDS_RefreshPushStatus()
{
    Class cls = FalconDS_Class(@"UNUserNotificationCenter",
                               "/System/Library/Frameworks/UserNotifications.framework/UserNotifications");
    id center = FalconDS_Send(cls, @"currentNotificationCenter");
    SEL selector = NSSelectorFromString(@"getNotificationSettingsWithCompletionHandler:");
    if (center == nil || ![center respondsToSelector:selector]) return;
    void (^handler)(id) = ^(id settings) {
        gPushStatus = (int)FalconDS_SendInteger(settings, @"authorizationStatus", -1);
    };
    ((void (*)(id, SEL, id))objc_msgSend)(center, selector, handler);
}

int FalconDS_PushStatus()
{
    return gPushStatus;
}

// ---- ATT (iOS 14+) ----

int FalconDS_AttStatus()
{
    Class cls = FalconDS_Class(@"ATTrackingManager",
                               "/System/Library/Frameworks/AppTrackingTransparency.framework/AppTrackingTransparency");
    return (int)FalconDS_SendInteger(cls, @"trackingAuthorizationStatus", -1);
}

// ---- MetricKit (iOS 14+): văng / treo của lần chạy trước ----

static volatile int gDiagnosticKind = 0; // 0 không có · 1 crash · 2 hang
static volatile long long gDiagnosticTimestampMs = 0;

@interface FalconDSMetricSubscriber : NSObject
- (void)didReceiveMetricPayloads:(NSArray *)payloads;
- (void)didReceiveDiagnosticPayloads:(NSArray *)payloads;
@end

@implementation FalconDSMetricSubscriber

- (void)didReceiveMetricPayloads:(NSArray *)payloads
{
}

- (void)didReceiveDiagnosticPayloads:(NSArray *)payloads
{
    @try
    {
        for (id payload in payloads)
        {
            id crashes = FalconDS_Send(payload, @"crashDiagnostics");
            id hangs = FalconDS_Send(payload, @"hangDiagnostics");
            int kind = 0;
            if ([crashes isKindOfClass:[NSArray class]] && [(NSArray *)crashes count] > 0) kind = 1;
            else if ([hangs isKindOfClass:[NSArray class]] && [(NSArray *)hangs count] > 0) kind = 2;
            if (kind == 0) continue;
            id end = FalconDS_Send(payload, @"timeStampEnd");
            if (![end isKindOfClass:[NSDate class]]) continue;
            long long ms = (long long)([(NSDate *)end timeIntervalSince1970] * 1000.0);
            if (ms < gDiagnosticTimestampMs) continue;
            gDiagnosticTimestampMs = ms;
            gDiagnosticKind = kind;
        }
    }
    @catch (NSException *exception)
    {
        // bỏ — báo cáo hỏng thì coi như không có
    }
}

@end

static FalconDSMetricSubscriber *gSubscriber = nil;

void FalconDS_StartMetricKit()
{
    if (gSubscriber != nil) return;
    Class cls = FalconDS_Class(@"MXMetricManager", "/System/Library/Frameworks/MetricKit.framework/MetricKit");
    id manager = FalconDS_Send(cls, @"sharedManager");
    SEL add = NSSelectorFromString(@"addSubscriber:");
    if (manager == nil || ![manager respondsToSelector:add]) return;

    // Khai giao thức lúc chạy (không import header MetricKit — tránh tự link framework)
    Protocol *protocol = objc_getProtocol("MXMetricManagerSubscriber");
    if (protocol != NULL) class_addProtocol([FalconDSMetricSubscriber class], protocol);

    gSubscriber = [[FalconDSMetricSubscriber alloc] init];
    ((void (*)(id, SEL, id))objc_msgSend)(manager, add, gSubscriber);
}

int FalconDS_LastDiagnosticKind()
{
    return gDiagnosticKind;
}

long long FalconDS_LastDiagnosticTimestampMs()
{
    return gDiagnosticTimestampMs;
}

}
