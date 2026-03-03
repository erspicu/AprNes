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

    // 用 requestAnimationFrame 驅動 C# game loop
    // dotNetRef: DotNetObjectReference，有 [JSInvokable] OnFrame()
    function startLoop(dotNetRef) {
        let running = true;
        let frameCount = 0;
        console.log('[AprNes] startLoop');
        function loop() {
            if (!running) return;
            dotNetRef.invokeMethodAsync('OnFrame')
                .then(() => { frameCount++; requestAnimationFrame(loop); })
                .catch(err => {
                    // OnFrame 例外不中斷 loop，繼續下一幀
                    console.warn('[AprNes] OnFrame error (frame ' + frameCount + '):', err);
                    requestAnimationFrame(loop);
                });
        }
        requestAnimationFrame(loop);
        return { stop: () => { running = false; console.log('[AprNes] loop stopped at frame', frameCount); } };
    }

    function focusCanvas() {
        if (canvas) canvas.focus();
    }

    return { init, drawFrame, playAudio, startLoop, focusCanvas };
})();
