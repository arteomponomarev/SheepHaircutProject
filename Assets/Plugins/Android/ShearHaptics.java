package com.shearandgrow.haptics;

import android.app.Activity;
import android.content.Context;
import android.content.pm.PackageManager;
import android.media.AudioAttributes;
import android.os.Build;
import android.os.VibrationAttributes;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.os.VibratorManager;
import android.provider.Settings;

/**
 * Shear & Grow haptics backend. Picks the best API the device really supports:
 *   1) Composition primitives (API 30+, CLICK/TICK[/THUD/LOW_TICK] supported)  -> crisp, scalable accents
 *   2) Amplitude-controlled one-shots + repeating waveform texture (API 26+)  -> strong, tunable
 *   3) Predefined effects (API 29+, no amplitude control)                     -> system-tuned clicks
 *   4) Plain on/off durations                                                 -> last resort
 * Vibrations use USAGE_MEDIA (API 33+) / AudioAttributes.USAGE_GAME (26-32) so system vibration-intensity
 * settings and "vibration off" modes are respected. Every call is guarded; errors never reach Unity.
 */
public final class ShearHaptics {
    public static final int FLAG_SUPPORTED = 1;
    public static final int FLAG_AMPLITUDE = 2;
    public static final int FLAG_COMPOSITION = 4;
    public static final int FLAG_THUD = 8;
    public static final int FLAG_LOW_TICK = 16;
    public static final int FLAG_PREDEFINED = 32;
    public static final int FLAG_PERMISSION = 64;
    public static final int FLAG_TOUCH_FEEDBACK_ON = 128;

    public static final int KIND_TEXTURE = 0, KIND_CONTACT = 1, KIND_CHUNK = 2;

    private static Vibrator vibrator;
    private static Context context;
    private static int sdk;
    private static int flags;
    private static Object vibrationAttributes; // VibrationAttributes (API 33+)
    private static AudioAttributes audioAttributes;  // API 26-32
    private static boolean textureRunning;
    private static int textureAmplitude = -1;
    private static String lastError = "";
    private static String path = "none";
    private static int calls;

    private ShearHaptics() {}

    public static int init(Activity activity) {
        try {
            context = activity.getApplicationContext();
            sdk = Build.VERSION.SDK_INT;
            if (sdk >= 31) {
                VibratorManager vm = (VibratorManager) context.getSystemService(Context.VIBRATOR_MANAGER_SERVICE);
                vibrator = vm != null ? vm.getDefaultVibrator() : null;
            } else {
                vibrator = (Vibrator) context.getSystemService(Context.VIBRATOR_SERVICE);
            }
            flags = 0;
            if (context.checkSelfPermission("android.permission.VIBRATE") == PackageManager.PERMISSION_GRANTED) flags |= FLAG_PERMISSION;
            if (vibrator == null || !vibrator.hasVibrator()) { path = "no vibrator"; return flags; }
            flags |= FLAG_SUPPORTED;
            if (vibrator.hasAmplitudeControl()) flags |= FLAG_AMPLITUDE;
            if (sdk >= 30 && vibrator.areAllPrimitivesSupported(VibrationEffect.Composition.PRIMITIVE_CLICK, VibrationEffect.Composition.PRIMITIVE_TICK))
                flags |= FLAG_COMPOSITION;
            if (sdk >= 31 && (flags & FLAG_COMPOSITION) != 0) {
                if (vibrator.areAllPrimitivesSupported(VibrationEffect.Composition.PRIMITIVE_THUD)) flags |= FLAG_THUD;
                if (vibrator.areAllPrimitivesSupported(VibrationEffect.Composition.PRIMITIVE_LOW_TICK)) flags |= FLAG_LOW_TICK;
            }
            if (sdk >= 29) flags |= FLAG_PREDEFINED;
            try {
                if (Settings.System.getInt(context.getContentResolver(), Settings.System.HAPTIC_FEEDBACK_ENABLED, 1) != 0)
                    flags |= FLAG_TOUCH_FEEDBACK_ON;
            } catch (Throwable ignored) { flags |= FLAG_TOUCH_FEEDBACK_ON; }

            if (sdk >= 33) vibrationAttributes = VibrationAttributes.createForUsage(VibrationAttributes.USAGE_MEDIA);
            else audioAttributes = new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_GAME)
                    .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build();

            if ((flags & FLAG_COMPOSITION) != 0) path = "composition primitives";
            else if ((flags & FLAG_AMPLITUDE) != 0) path = "amplitude one-shots + waveform";
            else if ((flags & FLAG_PREDEFINED) != 0) path = "predefined effects";
            else path = "on/off durations";
        } catch (Throwable t) {
            lastError = "init: " + t;
            flags &= ~FLAG_SUPPORTED;
        }
        return flags;
    }

    private static int amp(float v) {
        int a = Math.round(Math.max(0f, Math.min(1f, v)) * 255f);
        return Math.max(1, Math.min(255, a));
    }

    private static void play(VibrationEffect effect) {
        if (effect == null || vibrator == null) return;
        if (sdk >= 33 && vibrationAttributes != null) vibrator.vibrate(effect, (VibrationAttributes) vibrationAttributes);
        else vibrator.vibrate(effect, audioAttributes);
        calls++;
    }

    private static boolean has(int f) { return (flags & f) != 0; }

    /** Single accent. intensity/sharpness 0..1 (already profile-scaled in Unity). */
    public static void impact(int kind, float intensity, float sharpness) {
        if (!has(FLAG_SUPPORTED)) return;
        try {
            textureRunning = false; // any new vibrate() replaces the running waveform
            float i = Math.max(0f, Math.min(1f, intensity));
            if (has(FLAG_COMPOSITION)) {
                VibrationEffect.Composition c = VibrationEffect.startComposition();
                if (kind == KIND_TEXTURE) {
                    int prim = (sharpness < 0.4f && has(FLAG_LOW_TICK)) ? VibrationEffect.Composition.PRIMITIVE_LOW_TICK : VibrationEffect.Composition.PRIMITIVE_TICK;
                    c.addPrimitive(prim, i);
                } else if (kind == KIND_CONTACT) {
                    c.addPrimitive(VibrationEffect.Composition.PRIMITIVE_CLICK, i);
                    c.addPrimitive(VibrationEffect.Composition.PRIMITIVE_TICK, i * 0.45f, 28); // blade "bites" into the fleece
                } else {
                    if (has(FLAG_THUD)) c.addPrimitive(VibrationEffect.Composition.PRIMITIVE_THUD, i);
                    else c.addPrimitive(VibrationEffect.Composition.PRIMITIVE_CLICK, i);
                    c.addPrimitive(VibrationEffect.Composition.PRIMITIVE_CLICK, i * 0.6f, 35);
                }
                play(c.compose());
            } else if (has(FLAG_AMPLITUDE)) {
                // Durations long enough for LRA/ERM motors to actually spin up (the old 16 ms pulses were imperceptible).
                long ms = kind == KIND_TEXTURE ? 22 : kind == KIND_CONTACT ? 38 : 58;
                if (kind == KIND_TEXTURE) ms += Math.round(sharpness < 0.5f ? 6 : 0);
                play(VibrationEffect.createOneShot(ms, amp(i)));
            } else if (has(FLAG_PREDEFINED)) {
                int id = kind == KIND_TEXTURE ? VibrationEffect.EFFECT_TICK : kind == KIND_CONTACT ? VibrationEffect.EFFECT_CLICK : VibrationEffect.EFFECT_HEAVY_CLICK;
                play(VibrationEffect.createPredefined(id));
            } else {
                long ms = kind == KIND_TEXTURE ? 12 : kind == KIND_CONTACT ? 25 : 40;
                play(VibrationEffect.createOneShot(ms, VibrationEffect.DEFAULT_AMPLITUDE));
            }
        } catch (Throwable t) { lastError = "impact: " + t; }
    }

    /** Two distinct taps ("ta-dum"), different from cutting ticks. */
    public static void completion(float intensity, float sharpness, int gapMs) {
        if (!has(FLAG_SUPPORTED)) return;
        try {
            textureRunning = false;
            float i = Math.max(0f, Math.min(1f, intensity));
            if (has(FLAG_COMPOSITION)) {
                VibrationEffect.Composition c = VibrationEffect.startComposition();
                c.addPrimitive(VibrationEffect.Composition.PRIMITIVE_CLICK, i);
                if (has(FLAG_THUD)) c.addPrimitive(VibrationEffect.Composition.PRIMITIVE_THUD, i * 0.85f, gapMs);
                else c.addPrimitive(VibrationEffect.Composition.PRIMITIVE_CLICK, i * 0.85f, gapMs);
                play(c.compose());
            } else if (has(FLAG_AMPLITUDE)) {
                long[] t = {0, 45, gapMs, 60};
                int[] a = {0, amp(i), 0, amp(i * 0.85f)};
                play(VibrationEffect.createWaveform(t, a, -1));
            } else if (has(FLAG_PREDEFINED)) {
                play(VibrationEffect.createPredefined(VibrationEffect.EFFECT_DOUBLE_CLICK));
            } else {
                long[] t = {0, 40, gapMs, 50};
                play(VibrationEffect.createWaveform(t, -1));
            }
        } catch (Throwable t) { lastError = "completion: " + t; }
    }

    /** Native continuous texture (amplitude devices without primitives): a looping fibrous ripple, not a flat buzz. */
    public static void startTexture(float intensity, float sharpness) {
        if (!has(FLAG_SUPPORTED) || !has(FLAG_AMPLITUDE)) return;
        try {
            int a = amp(intensity);
            if (textureRunning && Math.abs(a - textureAmplitude) < 12) return; // avoid restarting for tiny changes
            long[] t = {20, 24, 16, 30};
            int[] amps = {a, Math.max(1, a * 30 / 100), Math.max(1, a * 80 / 100), Math.max(1, a * 22 / 100)};
            play(VibrationEffect.createWaveform(t, amps, 0));
            textureRunning = true;
            textureAmplitude = a;
        } catch (Throwable t) { lastError = "texture: " + t; }
    }

    public static void stop() {
        textureRunning = false;
        textureAmplitude = -1;
        try { if (vibrator != null) vibrator.cancel(); } catch (Throwable t) { lastError = "cancel: " + t; }
    }

    public static boolean touchFeedbackEnabled() {
        try {
            return context == null || Settings.System.getInt(context.getContentResolver(), Settings.System.HAPTIC_FEEDBACK_ENABLED, 1) != 0;
        } catch (Throwable t) { return true; }
    }

    public static int callCount() { return calls; }
    public static String lastError() { return lastError; }

    public static String info() {
        return "Android API " + sdk + ", path: " + path
                + ", amplitude=" + has(FLAG_AMPLITUDE) + ", primitives=" + has(FLAG_COMPOSITION)
                + ", thud=" + has(FLAG_THUD) + ", lowTick=" + has(FLAG_LOW_TICK)
                + ", VIBRATE perm=" + has(FLAG_PERMISSION) + ", touchFeedback=" + touchFeedbackEnabled();
    }
}
