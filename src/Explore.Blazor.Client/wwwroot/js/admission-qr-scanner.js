async function hasNativeQrSupport() {
    if (!globalThis.isSecureContext || typeof globalThis.BarcodeDetector !== "function") {
        return false;
    }

    const formats = await globalThis.BarcodeDetector.getSupportedFormats();
    return Array.isArray(formats) && formats.includes("qr_code");
}

export async function getCapability() {
    try {
        return { status: await hasNativeQrSupport() ? "supported" : "unsupported" };
    } catch {
        return { status: "unsupported" };
    }
}

export async function detect(imageSource) {
    let cameraStream = null;
    try {
        if (!imageSource || !await hasNativeQrSupport()) {
            return { status: "unsupported" };
        }

        if (imageSource.tagName === "VIDEO") {
            if (!globalThis.navigator?.mediaDevices?.getUserMedia) {
                return { status: "unsupported" };
            }

            cameraStream = await globalThis.navigator.mediaDevices.getUserMedia({
                audio: false,
                video: { facingMode: { ideal: "environment" } }
            });
            imageSource.srcObject = cameraStream;
            if (imageSource.readyState < 2) {
                await new Promise((resolve, reject) => {
                    imageSource.addEventListener("loadeddata", resolve, { once: true });
                    imageSource.addEventListener("error", reject, { once: true });
                });
            }
            await imageSource.play();
        }

        const detector = new globalThis.BarcodeDetector({ formats: ["qr_code"] });
        const results = await detector.detect(imageSource);
        if (!Array.isArray(results) || results.length === 0) {
            return { status: "noCode" };
        }
        if (results.length !== 1) {
            return { status: "multiple" };
        }

        return { status: "single", value: typeof results[0].rawValue === "string" ? results[0].rawValue : "" };
    } catch {
        return { status: "failure" };
    } finally {
        if (cameraStream) {
            for (const track of cameraStream.getTracks()) {
                track.stop();
            }
            imageSource.srcObject = null;
        }
    }
}
