import React, { useRef, useEffect, useState } from "react";
import { PaperPlaneRight, CircleNotch, Microphone, MicrophoneStage, Paperclip, X, FileXls } from "@phosphor-icons/react";
import { Button } from "@/components/ui/button";
import { motion, AnimatePresence } from "framer-motion";

interface ChatInputProps {
  value: string;
  onChange: (value: string) => void;
  onSend: (file: File | null) => void;
  isLoading: boolean;
}

export const ChatInput: React.FC<ChatInputProps> = ({ value, onChange, onSend, isLoading }) => {
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [isListening, setIsListening] = useState(false);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const recognitionRef = useRef<any>(null);

  useEffect(() => {
    if (textareaRef.current) {
      textareaRef.current.style.height = "auto";
      textareaRef.current.style.height = `${textareaRef.current.scrollHeight}px`;
    }
  }, [value]);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      const file = e.target.files[0];
      if (file.name.endsWith('.xlsx')) {
        setSelectedFile(file);
      } else {
        alert("Vui lòng chọn file Excel (.xlsx)");
      }
    }
  };

  const handleSendWrapper = () => {
    if ((!value.trim() && !selectedFile) || isLoading) return;
    onSend(selectedFile);
    setSelectedFile(null);
    if (fileInputRef.current) fileInputRef.current.value = "";
  };

  // Initialize Speech Recognition
  useEffect(() => {
    const SpeechRecognition = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition;
    if (SpeechRecognition) {
      recognitionRef.current = new SpeechRecognition();
      recognitionRef.current.continuous = false;
      recognitionRef.current.interimResults = true;
      recognitionRef.current.lang = "vi-VN"; // Default to Vietnamese

      recognitionRef.current.onresult = (event: any) => {
        const transcript = Array.from(event.results)
          .map((result: any) => result[0])
          .map((result: any) => result.transcript)
          .join("");

        onChange(transcript);
      };

      recognitionRef.current.onend = () => {
        setIsListening(false);
      };

      recognitionRef.current.onerror = (event: any) => {
        console.error("Speech recognition error", event.error);
        setIsListening(false);
      };
    }
  }, [onChange]);

  const toggleListening = () => {
    if (isListening) {
      recognitionRef.current?.stop();
    } else {
      try {
        recognitionRef.current?.start();
        setIsListening(true);
      } catch (err) {
        console.error("Start listening error:", err);
      }
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      if ((value.trim() || selectedFile) && !isLoading) {
        handleSendWrapper();
      }
    }
  };

  return (
    <div className="w-full z-50 animate-in fade-in slide-in-from-bottom-6 duration-1000 shrink-0">
      <div className={`glass-panel rounded-2xl sm:rounded-3xl p-1 sm:p-1.5 shadow-lg sm:shadow-2xl transition-all duration-500 border flex flex-col ${isListening
          ? "shadow-primary/40 border-primary/50 ring-2 sm:ring-4 ring-primary/20 bg-primary/5"
          : "shadow-primary/10 border-primary/10"
        }`}>

        <AnimatePresence>
          {selectedFile && (
            <motion.div
              initial={{ opacity: 0, height: 0, marginTop: 0 }}
              animate={{ opacity: 1, height: "auto", marginTop: 8 }}
              exit={{ opacity: 0, height: 0, marginTop: 0 }}
              className="px-3 sm:px-4 mb-1"
            >
              <div className="inline-flex items-center gap-2 bg-primary/10 border border-primary/20 pl-2 pr-1 py-1 rounded-lg">
                <FileXls size={18} weight="fill" className="text-emerald-500" />
                <span className="text-xs font-medium text-foreground max-w-[200px] truncate">{selectedFile.name}</span>
                <button
                  onClick={() => {
                    setSelectedFile(null);
                    if (fileInputRef.current) fileInputRef.current.value = "";
                  }}
                  className="w-5 h-5 flex items-center justify-center rounded-full hover:bg-black/10 transition-colors text-muted-foreground hover:text-foreground"
                >
                  <X size={12} weight="bold" />
                </button>
              </div>
            </motion.div>
          )}
        </AnimatePresence>

        <div className="relative flex items-center">
          <input
            type="file"
            ref={fileInputRef}
            onChange={handleFileChange}
            accept=".xlsx"
            className="hidden"
          />
          <textarea
            id="chat-input"
            ref={textareaRef}
            value={value}
            onChange={(e) => onChange(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={isListening ? "Đang lắng nghe..." : "Hỏi về cơ sở dữ liệu của bạn..."}
            className={`w-full min-h-[40px] sm:min-h-[48px] max-h-[150px] sm:max-h-[200px] py-2 sm:py-3 bg-transparent border-none focus-visible:ring-0 focus-visible:outline-none text-sm sm:text-base font-medium placeholder:text-muted-foreground/30 resize-none overflow-y-auto outline-none transition-all duration-300 ${isListening ? "pl-6 pr-24 sm:pl-8 sm:pr-32" : "pl-3 pr-[110px] sm:pl-6 sm:pr-[130px]"
              }`}
            rows={1}
            style={{ scrollbarWidth: "none" }}
          />

          <div className="absolute right-1.5 bottom-0.5 flex items-center gap-1.5">
            {/* Minimalist Sound Wave (To the left of mic button) */}
            <AnimatePresence>
              {isListening && (
                <motion.div
                  initial={{ opacity: 0, x: 10 }}
                  animate={{ opacity: 1, x: 0 }}
                  exit={{ opacity: 0, x: 10 }}
                  className="flex items-center gap-0.5 h-4 px-1"
                >
                  {[1, 2, 3, 4].map((i) => (
                    <motion.div
                      key={i}
                      animate={{
                        height: ["30%", "100%", "30%"],
                      }}
                      transition={{
                        duration: 0.4 + Math.random() * 0.4,
                        repeat: Infinity,
                        ease: "easeInOut",
                        delay: i * 0.1
                      }}
                      className="w-0.5 bg-primary rounded-full"
                    />
                  ))}
                </motion.div>
              )}
            </AnimatePresence>

            <div className="relative flex items-center justify-center">
              {/* Ripple Effect Layers */}
              {isListening && (
                <>
                  <div className="absolute w-12 h-12 bg-primary/20 rounded-full animate-ping" />
                  <div className="absolute w-14 h-14 bg-primary/10 rounded-full animate-pulse" />
                </>
              )}

              <Button
                type="button"
                onClick={toggleListening}
                variant="ghost"
                size="icon"
                className={`relative w-9 h-9 sm:w-11 sm:h-11 rounded-full transition-all duration-300 z-10 ${isListening
                    ? "text-white bg-primary shadow-lg shadow-primary/40 scale-105"
                    : "text-muted-foreground hover:text-primary hover:bg-primary/5"
                  }`}
              >
                {isListening ? (
                  <MicrophoneStage size={20} weight="fill" />
                ) : (
                  <Microphone size={20} />
                )}
              </Button>

              <Button
                type="button"
                onClick={() => fileInputRef.current?.click()}
                variant="ghost"
                size="icon"
                className="relative w-9 h-9 sm:w-11 sm:h-11 rounded-full text-muted-foreground hover:text-primary hover:bg-primary/5 transition-all duration-300 z-10"
                title="Đính kèm template Excel"
              >
                <Paperclip size={20} />
              </Button>
            </div>

            <Button
              id="send-button"
              onClick={handleSendWrapper}
              disabled={(!value.trim() && !selectedFile) || isLoading || isListening}
              size="icon"
              className="w-9 h-9 sm:w-11 sm:h-11 rounded-full bg-gradient-to-br from-primary via-primary to-primary/80 text-white shadow-lg shadow-primary/30 hover:shadow-primary/50 hover:scale-105 active:scale-95 transition-all duration-300 border border-white/20"
            >
              {isLoading ? (
                <CircleNotch size={20} className="animate-spin" />
              ) : (
                <PaperPlaneRight size={20} weight="bold" />
              )}
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
};
