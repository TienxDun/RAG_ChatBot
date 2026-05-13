/**
 * SpeechService.js - Manages browser Speech Recognition API
 */
export class SpeechService {
    constructor(options = {}) {
        this.lang = options.lang || 'vi-VN';
        this.onResult = options.onResult || (() => {});
        this.onEnd = options.onEnd || (() => {});
        this.onError = options.onError || (() => {});
        
        this.recognition = null;
        this.isListening = false;
        this._init();
    }

    _init() {
        const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (!SpeechRecognition) return;

        this.recognition = new SpeechRecognition();
        this.recognition.continuous = false;
        this.recognition.interimResults = true;
        this.recognition.lang = this.lang;

        this.recognition.onresult = (event) => {
            const transcript = Array.from(event.results)
                .map(result => result[0].transcript)
                .join('');
            this.onResult(transcript);
        };

        this.recognition.onend = () => {
            this.isListening = false;
            this.onEnd();
        };

        this.recognition.onerror = (event) => {
            console.error('Speech recognition error', event.error);
            this.isListening = false;
            this.onError(event.error);
        };
    }

    toggle() {
        if (this.isListening) {
            this.stop();
        } else {
            this.start();
        }
    }

    start() {
        if (!this.recognition || this.isListening) return;
        try {
            this.recognition.start();
            this.isListening = true;
        } catch (err) {
            console.error('Start recognition failed:', err);
        }
    }

    stop() {
        if (!this.recognition || !this.isListening) return;
        this.recognition.stop();
        this.isListening = false;
    }

    isSupported() {
        return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
    }
}
