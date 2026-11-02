// Shear & Grow — Core Haptics bridge for Unity (IL2CPP, [DllImport("__Internal")]).
// One CHHapticEngine is created and reused for the whole app lifetime. A single looping advanced player provides the
// continuous cutting texture (intensity / sharpness changed through dynamic parameters, never re-created per stamp).
// Transient accents are short patterns played on the same engine. stoppedHandler / resetHandler restore the engine
// after interruptions (backgrounding, audio session changes, server resets).
//
// Compiled with ARC (-fobjc-arc set by the plugin importer). Linked framework: CoreHaptics.

#import <Foundation/Foundation.h>
#import <CoreHaptics/CoreHaptics.h>
#include <string.h>
#include <stdlib.h>

static CHHapticEngine* s_engine = nil;
static id<CHHapticAdvancedPatternPlayer> s_continuous = nil;
static BOOL s_supported = NO;
static BOOL s_engineRunning = NO;
static BOOL s_continuousPlaying = NO;
static BOOL s_paused = NO;
static float s_contIntensity = 0.5f;
static float s_contSharpness = 0.5f;
static NSString* s_lastError = @"";
static int s_calls = 0;

static float Clamp01(float v) { return v < 0.f ? 0.f : (v > 1.f ? 1.f : v); }

static void SetError(NSString* where, NSError* error)
{
    if (error == nil) return;
    s_lastError = [NSString stringWithFormat:@"%@: %@ (%ld)", where, error.localizedDescription, (long)error.code];
    NSLog(@"[ShearHaptics] %@", s_lastError);
}

static BOOL EnsureEngineStarted(void)
{
    if (!s_supported || s_engine == nil || s_paused) return NO;
    if (s_engineRunning) return YES;
    NSError* error = nil;
    if ([s_engine startAndReturnError:&error]) { s_engineRunning = YES; return YES; }
    SetError(@"engine start", error);
    return NO;
}

// Looping continuous event with a built-in intensity ripple -> a fibrous texture rather than a flat buzz.
static void CreateContinuousPlayer(void)
{
    s_continuous = nil;
    if (s_engine == nil) return;
    NSError* error = nil;
    const NSTimeInterval period = 0.11;
    CHHapticEventParameter* intensity = [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity value:1.0f];
    CHHapticEventParameter* sharpness = [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness value:0.5f];
    CHHapticEvent* cont = [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous
                                                        parameters:@[intensity, sharpness]
                                                      relativeTime:0
                                                          duration:period];
    NSArray<CHHapticParameterCurveControlPoint*>* points = @[
        [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:0.000 value:1.00f],
        [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:0.030 value:0.55f],
        [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:0.055 value:0.85f],
        [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:0.085 value:0.45f],
        [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:period value:1.00f],
    ];
    CHHapticParameterCurve* ripple = [[CHHapticParameterCurve alloc] initWithParameterID:CHHapticDynamicParameterIDHapticIntensityControl
                                                                          controlPoints:points
                                                                           relativeTime:0];
    CHHapticPattern* pattern = [[CHHapticPattern alloc] initWithEvents:@[cont] parameterCurves:@[ripple] error:&error];
    if (pattern == nil) { SetError(@"continuous pattern", error); return; }
    s_continuous = [s_engine createAdvancedPlayerWithPattern:pattern error:&error];
    if (s_continuous == nil) { SetError(@"continuous player", error); return; }
    s_continuous.loopEnabled = YES;
    s_continuous.loopEnd = period;
}

static void SendContinuousParameters(void)
{
    if (s_continuous == nil) return;
    NSError* error = nil;
    CHHapticDynamicParameter* i = [[CHHapticDynamicParameter alloc] initWithParameterID:CHHapticDynamicParameterIDHapticIntensityControl value:s_contIntensity relativeTime:0];
    // Sharpness control is additive (-1..1) around the event's 0.5 sharpness.
    CHHapticDynamicParameter* s = [[CHHapticDynamicParameter alloc] initWithParameterID:CHHapticDynamicParameterIDHapticSharpnessControl value:(s_contSharpness - 0.5f) relativeTime:0];
    if (![s_continuous sendParameters:@[i, s] atTime:CHHapticTimeImmediate error:&error]) SetError(@"continuous params", error);
}

static void CreateEngine(void)
{
    NSError* error = nil;
    s_engine = [[CHHapticEngine alloc] initAndReturnError:&error];
    if (s_engine == nil) { SetError(@"engine create", error); s_supported = NO; return; }
    s_engine.playsHapticsOnly = YES;          // no audio resources; does not disturb Unity's audio session
    // No auto-shutdown: a synchronous restart on the first cut would add latency. Power is saved instead by
    // stopping the engine explicitly when the app is paused/backgrounded (ShearHaptics_SetPaused).
    s_engine.autoShutdownEnabled = NO;
    s_engine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) {
        s_engineRunning = NO;
        s_continuousPlaying = NO;
        if (reason != CHHapticEngineStoppedReasonIdleTimeout && reason != CHHapticEngineStoppedReasonApplicationSuspended)
            s_lastError = [NSString stringWithFormat:@"engine stopped, reason %ld", (long)reason];
    };
    s_engine.resetHandler = ^{
        // The haptic server restarted: every player is invalid. Restart engine and rebuild the reusable player.
        s_engineRunning = NO;
        s_continuousPlaying = NO;
        if (EnsureEngineStarted()) CreateContinuousPlayer();
    };
    if (EnsureEngineStarted()) CreateContinuousPlayer();
}

static void PlayTransients(const float* times, const float* intensities, const float* sharpnesses, int count)
{
    if (!EnsureEngineStarted()) return;
    NSMutableArray<CHHapticEvent*>* events = [NSMutableArray arrayWithCapacity:count];
    for (int k = 0; k < count; k++)
    {
        CHHapticEventParameter* i = [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity value:Clamp01(intensities[k])];
        CHHapticEventParameter* s = [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness value:Clamp01(sharpnesses[k])];
        [events addObject:[[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticTransient parameters:@[i, s] relativeTime:times[k]]];
    }
    NSError* error = nil;
    CHHapticPattern* pattern = [[CHHapticPattern alloc] initWithEvents:events parameters:@[] error:&error];
    if (pattern == nil) { SetError(@"transient pattern", error); return; }
    id<CHHapticPatternPlayer> player = [s_engine createPlayerWithPattern:pattern error:&error];
    if (player == nil) { SetError(@"transient player", error); return; }
    if (![player startAtTime:CHHapticTimeImmediate error:&error]) { SetError(@"transient start", error); return; }
    s_calls++;
}

static char* MakeUnityString(NSString* s)
{
    const char* utf8 = [s UTF8String];
    if (utf8 == NULL) utf8 = "";
    char* copy = (char*)malloc(strlen(utf8) + 1); // freed by the IL2CPP marshaller
    strcpy(copy, utf8);
    return copy;
}

extern "C" {

int ShearHaptics_Init(void)
{
    if (s_engine != nil) return s_supported ? 1 : 0;
    if (@available(iOS 13.0, *))
    {
        s_supported = CHHapticEngine.capabilitiesForHardware.supportsHaptics;
        if (!s_supported) { s_lastError = @"Device reports supportsHaptics = NO"; return 0; }
        CreateEngine();
        return (s_supported && s_engine != nil) ? 1 : 0;
    }
    s_lastError = @"Core Haptics requires iOS 13";
    return 0;
}

void ShearHaptics_Transient(float intensity, float sharpness)
{
    float t = 0.f;
    PlayTransients(&t, &intensity, &sharpness, 1);
}

void ShearHaptics_Double(float intensity, float sharpness, float gap)
{
    // second tap slightly softer & rounder so the completion reads as a "ta-dum", unlike cutting ticks
    float times[2] = { 0.f, gap };
    float ints[2] = { intensity, intensity * 0.8f };
    float sharps[2] = { sharpness, sharpness * 0.6f };
    PlayTransients(times, ints, sharps, 2);
}

void ShearHaptics_StartContinuous(float intensity, float sharpness)
{
    s_contIntensity = Clamp01(intensity);
    s_contSharpness = Clamp01(sharpness);
    if (!EnsureEngineStarted()) return;
    if (s_continuous == nil) CreateContinuousPlayer();
    if (s_continuous == nil) return;
    NSError* error = nil;
    if (!s_continuousPlaying)
    {
        if (![s_continuous startAtTime:CHHapticTimeImmediate error:&error]) { SetError(@"continuous start", error); return; }
        s_continuousPlaying = YES;
        s_calls++;
    }
    SendContinuousParameters();
}

void ShearHaptics_UpdateContinuous(float intensity, float sharpness)
{
    s_contIntensity = Clamp01(intensity);
    s_contSharpness = Clamp01(sharpness);
    if (!s_continuousPlaying || !s_engineRunning) return;
    SendContinuousParameters();
    s_calls++;
}

void ShearHaptics_StopContinuous(void)
{
    if (s_continuous == nil || !s_continuousPlaying) return;
    NSError* error = nil;
    if (![s_continuous stopAtTime:CHHapticTimeImmediate error:&error]) SetError(@"continuous stop", error);
    s_continuousPlaying = NO;
}

void ShearHaptics_StopAll(void)
{
    ShearHaptics_StopContinuous();
}

void ShearHaptics_SetPaused(int paused)
{
    s_paused = paused != 0;
    if (s_engine == nil) return;
    if (s_paused)
    {
        ShearHaptics_StopContinuous();
        [s_engine stopWithCompletionHandler:nil];
        s_engineRunning = NO;
    }
    else
    {
        // players created before an interruption may be invalid; rebuild the reusable one on resume
        if (EnsureEngineStarted()) CreateContinuousPlayer();
    }
}

void ShearHaptics_Shutdown(void)
{
    ShearHaptics_StopContinuous();
    s_continuous = nil;
    if (s_engine != nil) [s_engine stopWithCompletionHandler:nil];
    s_engine = nil;
    s_engineRunning = NO;
}

int ShearHaptics_CallCount(void) { return s_calls; }

char* ShearHaptics_LastError(void) { return MakeUnityString(s_lastError); }

char* ShearHaptics_Capabilities(void)
{
    NSString* caps = [NSString stringWithFormat:@"Core Haptics: supportsHaptics=%@, engine=%@, running=%@, continuous=%@",
                      s_supported ? @"YES" : @"NO", s_engine != nil ? @"created" : @"none",
                      s_engineRunning ? @"YES" : @"NO", s_continuous != nil ? @"ready" : @"none"];
    return MakeUnityString(caps);
}

}
