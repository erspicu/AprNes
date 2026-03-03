// nesInterop.js — Canvas 繪圖 + Web Audio 播放

window.nesInterop = (() => {
    let canvas = null;
    let ctx    = null;

    // ── Audio ─────────────────────────────────────────────────────────────────
    let audioCtx  = null;
    let nextTime  = 0;
    const SAMPLE_RATE = 44100;

    function ensureAudio() {
        if (audioCtx) return;
        audioCtx = new (window.AudioContext || window.webkitAudioContext)({ sampleRate: SAMPLE_RATE });
        nextTime = audioCtx.currentTime + 0.1;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    function init(canvasId) {
        canvas = document.getElementById(canvasId);
        ctx    = canvas.getContext('2d');
        canvas.focus();
        console.log('[AprNes] init canvas:', canvasId);
    }

    // pixels: Uint8Array (RGBA, 256×240×4 bytes) — unmarshalled 零拷貝版
    function drawFrameUnmarshalled(pixels) {
        if (!ctx) return true;
        const imageData = new ImageData(
            new Uint8ClampedArray(pixels.buffer, pixels.byteOffset, pixels.byteLength),
            256, 240);
        ctx.putImageData(imageData, 0, 0);
        return true;
    }

    // pixels: Uint8Array (RGBA, 256×240×4 bytes)
    function drawFrame(pixels) {
        if (!ctx) return;
        const imageData = new ImageData(new Uint8ClampedArray(pixels), 256, 240);
        ctx.putImageData(imageData, 0, 0);
    }

    // samples: Int16Array (44100 Hz mono)
    function playAudio(samples) {
        if (!samples || samples.length === 0) return;
        try {
            ensureAudio();
            // resume suspended context (browser autoplay policy)
            if (audioCtx.state === 'suspended') audioCtx.resume();

            const buf  = audioCtx.createBuffer(1, samples.length, SAMPLE_RATE);
            const data = buf.getChannelData(0);
            for (let i = 0; i < samples.length; i++)
                data[i] = samples[i] / 32768.0;

            const src = audioCtx.createBufferSource();
            src.buffer = buf;
            src.connect(audioCtx.destination);

            const now = audioCtx.currentTime;
            if (nextTime < now + 0.02) nextTime = now + 0.05; // re-sync if behind
            src.start(nextTime);
            nextTime += buf.duration;
        } catch (e) {
            // 音效例外不中斷遊戲
            console.warn('playAudio error:', e);
        }
    }

    // 用 requestAnimationFrame（限速）或 setTimeout(0)（不限速）驅動 C# game loop
    // dotNetRef: DotNetObjectReference，有 [JSInvokable] OnFrame()
    let loopFpsLimit = true;

    function setFpsLimit(val) { loopFpsLimit = val; }

    function startLoop(dotNetRef) {
        let running = true;
        console.log('[AprNes] startLoop (sync invokeMethod)');
        function loop() {
            if (!running) return;
            try {
                dotNetRef.invokeMethod('OnFrame'); // 同步呼叫，無 Promise overhead
            } catch(err) {
                console.warn('[AprNes] OnFrame error:', err);
            }
            if (loopFpsLimit) requestAnimationFrame(loop);
            else setTimeout(loop, 0);
        }
        if (loopFpsLimit) requestAnimationFrame(loop);
        else setTimeout(loop, 0);
        return { stop: () => { running = false; } };
    }

    function focusCanvas() {
        if (canvas) canvas.focus();
    }

    // ── Gamepad ───────────────────────────────────────────────────────────────
    let gamepadDotNetRef = null;

    window.addEventListener('gamepadconnected', (e) => {
        console.log('[AprNes] gamepad connected:', e.gamepad.id);
        if (gamepadDotNetRef) gamepadDotNetRef.invokeMethodAsync('OnGamepadConnected', e.gamepad.id);
    });
    window.addEventListener('gamepaddisconnected', (e) => {
        console.log('[AprNes] gamepad disconnected');
        if (gamepadDotNetRef) gamepadDotNetRef.invokeMethodAsync('OnGamepadDisconnected');
    });

    function setGamepadCallback(dotNetRef) {
        gamepadDotNetRef = dotNetRef;
    }

    // 回傳 8-bit mask（bit0=A, 1=B, 2=Select, 3=Start, 4=Up, 5=Down, 6=Left, 7=Right）
    // 若無手把連接回傳 -1
    function getGamepadState() {
        const gamepads = navigator.getGamepads ? navigator.getGamepads() : [];
        if (!gamepads) return -1;
        // 找第一個已連接的手把
        let gp = null;
        for (let i = 0; i < gamepads.length; i++) {
            if (gamepads[i] && gamepads[i].connected) { gp = gamepads[i]; break; }
        }
        if (!gp) return -1;

        let mask = 0;
        const btn = (idx) => gp.buttons[idx]?.pressed ?? false;
        const ax  = gp.axes;

        if (btn(0))                        mask |= (1 << 0); // A
        if (btn(1) || btn(2))              mask |= (1 << 1); // B (B 或 X 鍵)
        if (btn(8))                        mask |= (1 << 2); // Select
        if (btn(9))                        mask |= (1 << 3); // Start
        if (btn(12) || (ax[1] ?? 0) < -0.5) mask |= (1 << 4); // Up
        if (btn(13) || (ax[1] ?? 0) >  0.5) mask |= (1 << 5); // Down
        if (btn(14) || (ax[0] ?? 0) < -0.5) mask |= (1 << 6); // Left
        if (btn(15) || (ax[0] ?? 0) >  0.5) mask |= (1 << 7); // Right
        return mask;
    }

    return { init, drawFrame, drawFrameUnmarshalled, playAudio, startLoop, focusCanvas, setFpsLimit, getGamepadState, setGamepadCallback };
})();
