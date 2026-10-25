package com.controllerbridge.app;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.view.View;

import java.util.Map;

/** A read-only monitor of the selected controller's mapped input. */
final class ControllerPreview extends View {
    private static final int CYAN = 0xff52d8e8;
    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF rect = new RectF();
    private Map<String, Boolean> buttons;
    private Map<String, Float> axes;
    private boolean dualShock;

    ControllerPreview(Context context) {
        super(context);
        setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        setLayerType(View.LAYER_TYPE_SOFTWARE, null);
    }

    void setOutput(boolean dualShock) { this.dualShock = dualShock; invalidate(); }

    void setInput(Map<String, Boolean> buttons, Map<String, Float> axes) {
        this.buttons = buttons; this.axes = axes; invalidate();
    }

    private boolean down(String key) { return buttons != null && Boolean.TRUE.equals(buttons.get(key)); }
    private float axis(String key) {
        Float value = axes == null ? null : axes.get(key);
        return value == null ? 0 : Math.max(-1, Math.min(1, value));
    }

    @Override protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        float scale = Math.min(getWidth() / 360f, getHeight() / 240f);
        canvas.save();
        canvas.translate((getWidth() - 360 * scale) / 2, (getHeight() - 240 * scale) / 2);
        canvas.scale(scale, scale);
        paint.setStyle(Paint.Style.FILL); paint.setColor(0x20000000);
        rect.set(49, 201, 311, 223); canvas.drawOval(rect, paint);

        shoulder(canvas, 62, 24, "l2", "lt", dualShock ? "L2" : "LT");
        shoulder(canvas, 246, 24, "r2", "rt", dualShock ? "R2" : "RT");
        Path body = new Path();
        body.moveTo(86, 46); body.cubicTo(51, 46, 40, 73, 31, 107);
        body.cubicTo(22, 137, 14, 189, 31, 208);
        body.cubicTo(48, 226, 72, 187, 91, 171);
        body.cubicTo(130, 187, 230, 187, 269, 171);
        body.cubicTo(288, 187, 312, 226, 329, 208);
        body.cubicTo(346, 189, 338, 137, 329, 107);
        body.cubicTo(320, 73, 309, 46, 274, 46);
        body.cubicTo(253, 47, 240, 64, 223, 65);
        body.lineTo(137, 65); body.cubicTo(120, 64, 107, 47, 86, 46); body.close();
        paint.setStyle(Paint.Style.FILL); paint.setColor(0xff252c2f); canvas.drawPath(body, paint);
        paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(1.4f); paint.setColor(0xff657176); canvas.drawPath(body, paint);

        bumper(canvas, 77, "l1", dualShock ? "L1" : "LB");
        bumper(canvas, 263, "r1", dualShock ? "R1" : "RB");

        if (dualShock) {
            // The DS4 has symmetric sticks, an upper D-pad and a distinct center touchpad.
            circle(canvas, 116, 157, 29, 0xff171c1e, 0xff49555a);
            stick(canvas, 116, 157, axis("lx"), axis("ly"), down("l3"));
            circle(canvas, 244, 157, 29, 0xff171c1e, 0xff49555a);
            stick(canvas, 244, 157, axis("rx"), axis("ry"), down("r3"));
            dpad(canvas, 78, 107);
            rect.set(145, 73, 215, 113); paint.setStyle(Paint.Style.FILL); paint.setColor(0xff192023);
            canvas.drawRoundRect(rect, 7, 7, paint);
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(1); paint.setColor(0xff56646a); canvas.drawRoundRect(rect, 7, 7, paint);
            paint.setColor(0xff3d4d54); paint.setStrokeWidth(2); canvas.drawLine(153, 69, 207, 69, paint);
            centerButton(canvas, 130, 92, "share", "−");
            centerButton(canvas, 230, 92, "options", "≡");
            circle(canvas, 180, 137, 11, down("ps") ? 0xff28444a : 0xff192023, down("ps") ? CYAN : 0xff5c696e);
            label(canvas, "PS", 180, 140, 8, down("ps") ? CYAN : 0xffc3cdd1);
        } else {
            circle(canvas, 91, 105, 31, 0xff171c1e, 0xff49555a);
            stick(canvas, 91, 105, axis("lx"), axis("ly"), down("l3"));
            circle(canvas, 229, 157, 29, 0xff171c1e, 0xff49555a);
            stick(canvas, 229, 157, axis("rx"), axis("ry"), down("r3"));
            dpad(canvas, 133, 155);
            centerButton(canvas, 151, 101, "share", "−");
            centerButton(canvas, 209, 101, "options", "≡");
            circle(canvas, 180, 96, 14, down("ps") ? 0xff28444a : 0xff192023, down("ps") ? CYAN : 0xff5c696e);
            label(canvas, "CB", 180, 100, 9, down("ps") ? CYAN : 0xffc3cdd1);
        }
        face(canvas, 276, 83, "north", dualShock ? "△" : "Y");
        face(canvas, 252, 107, "west", dualShock ? "□" : "X");
        face(canvas, 300, 107, "east", dualShock ? "○" : "B");
        face(canvas, 276, 131, "south", dualShock ? "×" : "A");
        label(canvas, buttons == null ? "—" : "●", 180, 210, 10, buttons == null ? 0xff657176 : CYAN);
        canvas.restore();
    }

    private void shoulder(Canvas canvas, float x, float y, String button, String axis, String label) {
        boolean active = down(button) || this.axis(axis) > .05f;
        rect.set(x, y, x + 52, y + 21);
        paint.setStyle(Paint.Style.FILL); paint.setColor(active ? 0xff24474e : 0xff242c2f); canvas.drawRoundRect(rect, 7, 7, paint);
        paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(1); paint.setColor(active ? CYAN : 0xff566267); canvas.drawRoundRect(rect, 7, 7, paint);
        if (this.axis(axis) > .05f) {
            paint.setStyle(Paint.Style.FILL); paint.setColor(CYAN);
            rect.set(x + 4, y + 17, x + 4 + 44 * Math.max(0, this.axis(axis)), y + 19); canvas.drawRoundRect(rect, 1, 1, paint);
        }
        label(canvas, label, x + 26, y + 13, 9, active ? CYAN : 0xffbac5c9);
    }

    private void bumper(Canvas canvas, float x, String button, String label) {
        rect.set(x - 16, 51, x + 16, 67);
        paint.setStyle(Paint.Style.FILL); paint.setColor(down(button) ? 0xff31565e : 0xff192023);
        canvas.drawRoundRect(rect, 5, 5, paint);
        label(canvas, label, x, 62, 8, down(button) ? CYAN : 0xffbac5c9);
    }

    private void stick(Canvas canvas, float x, float y, float horizontal, float vertical, boolean pressed) {
        boolean active = pressed || Math.abs(horizontal) > .08 || Math.abs(vertical) > .08;
        float cx = x + horizontal * 9, cy = y + vertical * 9;
        circle(canvas, cx, cy, 21, 0xff293236, active ? CYAN : 0xff7a888e);
        circle(canvas, cx, cy, 16, 0xff222a2e, active ? 0xff43727b : 0xff414d53);
        if (active) circle(canvas, cx, cy, 3, CYAN, CYAN);
    }

    private void dpad(Canvas canvas, float x, float y) {
        dpadPart(canvas, x - 7, y - 25, x + 7, y - 5, down("dpad_up"));
        dpadPart(canvas, x - 7, y + 5, x + 7, y + 25, down("dpad_down"));
        dpadPart(canvas, x - 25, y - 7, x - 5, y + 7, down("dpad_left"));
        dpadPart(canvas, x + 5, y - 7, x + 25, y + 7, down("dpad_right"));
        paint.setStyle(Paint.Style.FILL); paint.setColor(0xff263034); rect.set(x - 8, y - 8, x + 8, y + 8); canvas.drawRect(rect, paint);
    }

    private void dpadPart(Canvas canvas, float left, float top, float right, float bottom, boolean active) {
        rect.set(left, top, right, bottom); paint.setStyle(Paint.Style.FILL); paint.setColor(active ? 0xff31565e : 0xff263034);
        canvas.drawRoundRect(rect, 3, 3, paint); paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(1); paint.setColor(active ? CYAN : 0xff748187);
        canvas.drawRoundRect(rect, 3, 3, paint);
    }

    private void face(Canvas canvas, float x, float y, String key, String glyph) {
        boolean active = down(key);
        if (active) { paint.setShadowLayer(9, 0, 0, 0x8852d8e8); }
        circle(canvas, x, y, 14, active ? 0xff25444b : 0xff182024, active ? CYAN : 0xff626f75);
        paint.clearShadowLayer(); label(canvas, glyph, x, y + 5, 16, active ? CYAN : 0xffd2dadd);
    }

    private void centerButton(Canvas canvas, float x, float y, String key, String glyph) {
        circle(canvas, x, y, 9, 0xff1c2529, down(key) ? CYAN : 0xff5c696e);
        label(canvas, glyph, x, y + 4, 12, down(key) ? CYAN : 0xffb0bec3);
    }

    private void circle(Canvas canvas, float x, float y, float radius, int fill, int stroke) {
        paint.setStyle(Paint.Style.FILL); paint.setColor(fill); canvas.drawCircle(x, y, radius, paint);
        paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(1.2f); paint.setColor(stroke); canvas.drawCircle(x, y, radius, paint);
    }

    private void label(Canvas canvas, String value, float x, float y, float size, int color) {
        paint.setStyle(Paint.Style.FILL); paint.setColor(color); paint.setTextSize(size); paint.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
        paint.setTextAlign(Paint.Align.CENTER); canvas.drawText(value, x, y, paint);
    }
}
