#import <Foundation/Foundation.h>
#include <dispatch/dispatch.h>
#include <errno.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <xpc/xpc.h>

// Private Apple protocol, verified with RemotePairing 280.40.3 on macOS 27.2.
// Keep it in a separate process: EOF, cancellation, timeout, or exit withdraws the beacon.
static xpc_connection_t service;
static xpc_connection_t listener;
static xpc_connection_t callback;
static dispatch_source_t input_source;
static dispatch_source_t interrupt_source;
static dispatch_source_t terminate_source;
static BOOL ended;
static BOOL code_shown;

static void cancel_connections(void)
{
    if (callback != NULL) xpc_connection_cancel(callback);
    if (service != NULL) xpc_connection_cancel(service);
    if (listener != NULL) xpc_connection_cancel(listener);
}

static void emit(NSDictionary *event)
{
    NSData *data = [NSJSONSerialization dataWithJSONObject:event options:0 error:NULL];
    if (data == nil || fwrite(data.bytes, 1, data.length, stdout) != data.length ||
        fputc('\n', stdout) == EOF || fflush(stdout) == EOF) {
        cancel_connections();
        exit(1);
    }
}

static void finish_event(NSDictionary *event, int status)
{
    if (ended) return;
    ended = YES;
    emit(event);
    cancel_connections();
    exit(status);
}

static void finish(NSString *kind, int status)
{
    finish_event(@{@"kind": kind}, status);
}

static void fail(NSString *reason)
{
    finish_event(@{@"kind": @"error", @"reason": reason}, 1);
}

static NSString *safe_string(xpc_object_t dictionary, const char *key, NSUInteger maximum)
{
    xpc_object_t item = xpc_dictionary_get_value(dictionary, key);
    if (item == NULL || xpc_get_type(item) != XPC_TYPE_STRING) return nil;
    const char *bytes = xpc_string_get_string_ptr(item);
    if (bytes == NULL) return nil;
    NSString *value = [NSString stringWithUTF8String:bytes];
    if (value == nil || value.length == 0 || value.length > maximum ||
        [value stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet].length == 0 ||
        [value rangeOfCharacterFromSet:NSCharacterSet.controlCharacterSet].location != NSNotFound)
        return nil;
    return value;
}

static void handle_event(xpc_connection_t peer, xpc_object_t event)
{
    if (xpc_get_type(event) != XPC_TYPE_DICTIONARY ||
        xpc_dictionary_get_value(event, "error") != NULL) {
        finish(@"error", 1);
        return;
    }
    const char *type = xpc_dictionary_get_string(event, "mangledTypeName");
    if (type == NULL ||
        strcmp(type, "RemotePairing.CodableDeviceInitiatedPairingBeaconingSessionEvent") != 0) {
        finish(@"error", 1);
        return;
    }
    xpc_object_t value = xpc_dictionary_get_value(event, "value");
    if (value == NULL || xpc_get_type(value) != XPC_TYPE_DICTIONARY) {
        finish(@"error", 1);
        return;
    }
    xpc_object_t reply = xpc_dictionary_create_reply(event);
    if (reply != NULL) {
        xpc_dictionary_set_value(reply, "response", xpc_dictionary_create(NULL, NULL, 0));
        xpc_connection_send_message(peer, reply);
    }

    xpc_object_t show = xpc_dictionary_get_value(value, "show");
    if (show != NULL) {
        if (xpc_get_type(show) != XPC_TYPE_DICTIONARY) {
            finish(@"error", 1);
            return;
        }
        NSString *pin = safe_string(show, "pin", 128);
        if (pin == nil) {
            finish(@"error", 1);
            return;
        }
        code_shown = YES;
        // The only sensitive channel is a pipe consumed by Sherpa's pairing dialog.
        // Never print native event descriptions, error payloads, or codes to stderr.
        emit(@{@"kind": @"code", @"code": pin,
               @"device": safe_string(show, "sendingDevice", 256) ?: @""});
        return;
    }
    if (xpc_dictionary_get_value(value, "failure") != NULL) {
        finish(@"error", 1);
    } else if (xpc_dictionary_get_value(value, "success") != NULL) {
        finish(code_shown ? @"success" : @"error", code_shown ? 0 : 1);
    } else if (xpc_dictionary_get_value(value, "empty") == NULL) {
        finish(@"error", 1);
    }
}

static void watch_lifetime(dispatch_queue_t queue, unsigned int timeout)
{
    signal(SIGINT, SIG_IGN);
    signal(SIGTERM, SIG_IGN);
    interrupt_source = dispatch_source_create(DISPATCH_SOURCE_TYPE_SIGNAL, SIGINT, 0, queue);
    terminate_source = dispatch_source_create(DISPATCH_SOURCE_TYPE_SIGNAL, SIGTERM, 0, queue);
    dispatch_source_set_event_handler(interrupt_source, ^{ finish(@"cancelled", 2); });
    dispatch_source_set_event_handler(terminate_source, ^{ finish(@"cancelled", 2); });
    dispatch_resume(interrupt_source);
    dispatch_resume(terminate_source);

    // The parent owns this pipe. Its closure also stops pairing if Sherpa crashes.
    input_source = dispatch_source_create(DISPATCH_SOURCE_TYPE_READ, STDIN_FILENO, 0, queue);
    dispatch_source_set_event_handler(input_source, ^{
        char buffer[32];
        ssize_t count = read(STDIN_FILENO, buffer, sizeof(buffer));
        if (count >= 0 || errno != EINTR) finish(@"cancelled", 2);
    });
    dispatch_resume(input_source);
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)timeout * NSEC_PER_SEC), queue, ^{
        finish(@"timedOut", 2);
    });
}

static void advertise(unsigned int timeout)
{
    dispatch_queue_t queue = dispatch_queue_create("codes.redth.sherpa.apple-pairing", DISPATCH_QUEUE_SERIAL);
    watch_lifetime(queue, timeout);
    listener = xpc_connection_create(NULL, queue);
    if (listener == NULL) {
        finish(@"error", 1);
        return;
    }
    xpc_connection_set_event_handler(listener, ^(xpc_object_t incoming) {
        if (xpc_get_type(incoming) != XPC_TYPE_CONNECTION || callback != NULL) {
            finish(@"error", 1);
            return;
        }
        callback = incoming;
        xpc_connection_set_target_queue(callback, queue);
        xpc_connection_set_event_handler(callback, ^(xpc_object_t event) {
            handle_event(callback, event);
        });
        xpc_connection_activate(callback);
    });
    xpc_connection_activate(listener);
    xpc_endpoint_t endpoint = xpc_endpoint_create(listener);
    if (endpoint == NULL) {
        finish(@"error", 1);
        return;
    }
    service = xpc_connection_create("com.apple.CoreDevice.remotepairingd", queue);
    if (service == NULL) {
        fail(@"serviceUnavailable");
        return;
    }
    xpc_connection_set_event_handler(service, ^(xpc_object_t event) {
        fail(xpc_get_type(event) == XPC_TYPE_ERROR ? @"serviceUnavailable" : @"unexpectedServiceEvent");
    });
    xpc_connection_activate(service);

    xpc_object_t body = xpc_dictionary_create(NULL, NULL, 0);
    xpc_dictionary_set_value(body, "endpoint", endpoint);
    xpc_object_t request = xpc_dictionary_create(NULL, NULL, 0);
    xpc_dictionary_set_string(request, "mangledTypeName",
        "RemotePairing.CreateDeviceInitiatedPairingBeaconingSessionRequest");
    xpc_dictionary_set_value(request, "value", body);
    xpc_connection_send_message_with_reply(service, request, queue, ^(xpc_object_t reply) {
        if (xpc_get_type(reply) != XPC_TYPE_DICTIONARY ||
            xpc_dictionary_get_value(reply, "error") != NULL) {
            finish(@"error", 1);
            return;
        }
        xpc_object_t response = xpc_dictionary_get_value(reply, "response");
        if (response == NULL || xpc_get_type(response) != XPC_TYPE_DICTIONARY) {
            finish(@"error", 1);
            return;
        }
        emit(@{@"kind": @"advertising"});
    });

    dispatch_main();
}

static void probe(void)
{
    dispatch_queue_t queue = dispatch_queue_create("codes.redth.sherpa.apple-pairing-probe", DISPATCH_QUEUE_SERIAL);
    service = xpc_connection_create("com.apple.CoreDevice.remotepairingd", queue);
    if (service == NULL) {
        fail(@"serviceUnavailable");
        return;
    }
    xpc_connection_set_event_handler(service, ^(xpc_object_t event) {
        // Browse can deliver unsolicited device updates in addition to its reply.
        if (xpc_get_type(event) == XPC_TYPE_ERROR) fail(@"serviceUnavailable");
    });
    xpc_connection_activate(service);
    xpc_object_t body = xpc_dictionary_create(NULL, NULL, 0);
    xpc_dictionary_set_bool(body, "currentDevicesOnly", true);
    xpc_object_t request = xpc_dictionary_create(NULL, NULL, 0);
    xpc_dictionary_set_string(request, "mangledTypeName", "RemotePairing.BrowseRequest");
    xpc_dictionary_set_value(request, "value", body);
    xpc_connection_send_message_with_reply(service, request, queue, ^(xpc_object_t reply) {
        if (xpc_get_type(reply) != XPC_TYPE_DICTIONARY) fail(@"invalidServiceReply");
        else if (xpc_dictionary_get_value(reply, "error") != NULL) fail(@"nativeFailure");
        else if (xpc_dictionary_get_value(reply, "response") == NULL) fail(@"unsupportedServiceReply");
        else finish(@"available", 0);
    });
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, 10 * NSEC_PER_SEC), queue, ^{
        fail(@"serviceTimedOut");
    });
    dispatch_main();
}

static int self_test(void)
{
    xpc_object_t show = xpc_dictionary_create(NULL, NULL, 0);
    for (NSString *text in @[@"123456", @"123-456", @"A1B2 C3D4",
        [@"" stringByPaddingToLength:128 withString:@"1" startingAtIndex:0]]) {
        xpc_dictionary_set_string(show, "pin", text.UTF8String);
        if (![safe_string(show, "pin", 128) isEqualToString:text]) return 1;
    }
    for (NSString *text in @[@"", @"   ", @"123\n456",
        [@"" stringByPaddingToLength:129 withString:@"1" startingAtIndex:0]]) {
        xpc_dictionary_set_string(show, "pin", text.UTF8String);
        if (safe_string(show, "pin", 128) != nil) return 1;
    }
    xpc_dictionary_set_int64(show, "pin", 123456);
    if (safe_string(show, "pin", 128) != nil) return 1;
    xpc_dictionary_set_value(show, "pin", NULL);
    xpc_dictionary_set_string(show, "code", "123456");
    xpc_dictionary_set_string(show, "_0", "123456");
    if (safe_string(show, "pin", 128) != nil) return 1;
    emit(@{@"kind": @"selfTestPassed"});
    return 0;
}

int main(int argc, const char *argv[])
{
    @autoreleasepool {
        signal(SIGPIPE, SIG_IGN);
        if (argc == 2 && strcmp(argv[1], "--self-test") == 0) return self_test();
        if (argc == 2 && strcmp(argv[1], "--self-test-lifetime") == 0) {
            // Exercise the real EOF/signal/deadline handlers without loading RemotePairing.
            dispatch_queue_t queue = dispatch_queue_create("codes.redth.sherpa.apple-pairing-test", DISPATCH_QUEUE_SERIAL);
            watch_lifetime(queue, 1);
            emit(@{@"kind": @"selfTestWaiting"});
            dispatch_main();
        }
        BOOL read_only = argc == 2 && strcmp(argv[1], "--probe") == 0;
        BOOL confirmed = argc == 5 && strcmp(argv[1], "--advertise") == 0 &&
            strcmp(argv[2], "--confirm") == 0 && strcmp(argv[3], "--timeout") == 0;
        unsigned long timeout = 0;
        if (confirmed) {
            char *end = NULL;
            errno = 0;
            timeout = strtoul(argv[4], &end, 10);
            confirmed = errno == 0 && end != argv[4] && *end == '\0' &&
                timeout >= 15 && timeout <= 300 && !isatty(STDIN_FILENO) && !isatty(STDOUT_FILENO);
        }
        if (!read_only && !confirmed) {
            fprintf(stderr, "Use --self-test, --probe, or --advertise --confirm --timeout <15-300>. Advertising requires Sherpa-owned pipes.\n");
            return 1;
        }
        NSBundle *framework = [NSBundle bundleWithPath:
            @"/Library/Apple/System/Library/PrivateFrameworks/RemotePairing.framework"];
        if (framework == nil || ![framework loadAndReturnError:NULL]) {
            fail(@"frameworkUnavailable");
        }
        // Loading the framework is required before connecting to its bundled XPC service.
        if (read_only) probe();
        else advertise((unsigned int)timeout);
        return 1;
    }
}
