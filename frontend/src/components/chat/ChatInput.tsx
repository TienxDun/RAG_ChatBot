import React, { useRef, useEffect, useState } from "react";
import { PaperPlaneRight, CircleNotch, Microphone, MicrophoneStage } from "@phosphor-icons/react";
import { Button } from "@/components/ui/button";
import { motion, AnimatePresence } from "framer-motion";

interface ChatInputProps {
  value: string;
  onChange: (value: string) => void;
  onSend: () => void;
  isLoading: boolean;
}

export const ChatInput: React.FC<ChatInputProps> = ({ value, onChange, onSend, isLoading }) => {
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const [isListening, setIsListening] = useState(false);
  const recognitionRef = useRef<any>(null);

  useEffect(() => {
    if (textareaRef.current) {
      textareaRef.current.style.height = "auto";
      textareaRef.current.style.height = `${textareaRef.current.scrollHeight}px`;
    }
  }, [value]);

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
      if (value.trim() && !isLoading) {
        onSend();
      }
    }
  };

  return (
    <div className="w-full z-50 animate-in fade-in slide-in-from-bottom-6 duration-1000 shrink-0">
      <div className={`glass-panel rounded-3xl p-1.5 shadow-2xl transition-all duration-500 border ${
        isListening 
          ? "shadow-primary/40 border-primary/50 ring-4 ring-primary/20 bg-primary/5" 
          : "shadow-primary/10 border-primary/10"
      }`}>
        <div className="relative flex items-center">
          <textarea
            id="chat-input"
            ref={textareaRef}
            value={value}
            onChange={(e) => onChange(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={isListening ? "Đang lắng nghe..." : "Hỏi về cơ sở dữ liệu của bạn..."}
            className={`w-full min-h-[48px] max-h-[200px] py-3 bg-transparent border-none focus-visible:ring-0 focus-visible:outline-none text-base font-medium placeholder:text-muted-foreground/30 resize-none overflow-y-auto outline-none transition-all duration-300 ${
              isListening ? "pl-8 pr-32" : "pl-6 pr-24"
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
                className={`relative w-11 h-11 rounded-full transition-all duration-300 z-10 ${
                  isListening 
                    ? "text-white bg-primary shadow-lg shadow-primary/40 scale-105" 
                    : "text-muted-foreground hover:text-primary hover:bg-primary/5"
                }`}
              >
                {isListening ? (
                  <MicrophoneStage size={22} weight="fill" />
                ) : (
                  <Microphone size={22} />
                )}
              </Button>
            </div>

            <Button
              onClick={onSend}
              disabled={!value.trim() || isLoading || isListening}
              size="icon"
              className="w-11 h-11 rounded-full bg-gradient-to-br from-primary via-primary to-primary/80 text-white shadow-lg shadow-primary/30 hover:shadow-primary/50 hover:scale-105 active:scale-95 transition-all duration-300 border border-white/20"
            >
              {isLoading ? (
                <CircleNotch size={22} className="animate-spin" />
              ) : (
                <PaperPlaneRight size={22} weight="bold" />
              )}
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
};
