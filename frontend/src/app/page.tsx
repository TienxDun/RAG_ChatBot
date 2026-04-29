"use client";

import * as React from "react";
import { ChatService, type Message } from "@/lib/chat-service";
import { useChatHistory } from "@/lib/use-chat-history";
import { Sidebar } from "@/components/chat/Sidebar";
import { ChatHeader } from "@/components/chat/ChatHeader";
import { ChatMessage } from "@/components/chat/ChatMessage";
import { ChatInput } from "@/components/chat/ChatInput";
import StarfieldBackground from "@/components/ui/StarfieldBackground";
import { motion, AnimatePresence } from "framer-motion";

export default function Home() {
  const { 
    sessions, 
    currentSession, 
    currentSessionId, 
    createNewChat, 
    saveChat, 
    deleteSession, 
    selectSession 
  } = useChatHistory();

  const [inputValue, setInputValue] = React.useState("");
  const [messages, setMessages] = React.useState<Message[]>([]);
  const [isLoading, setIsLoading] = React.useState(false);
  const [isSidebarOpen, setIsSidebarOpen] = React.useState(false);
  const scrollRef = React.useRef<HTMLDivElement>(null);

  // Sync messages when current session changes
  React.useEffect(() => {
    if (currentSession) {
      setMessages(currentSession.messages);
    } else {
      setMessages([]);
    }
  }, [currentSessionId, currentSession]);

  React.useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages]);

  const handleSend = async () => {
    if (!inputValue.trim() || isLoading) return;

    const userMessage = inputValue.trim();
    setInputValue("");
    
    const newMessages: Message[] = [...messages, { role: "user", content: userMessage }];
    setMessages(newMessages);
    setIsLoading(true);

    try {
      const assistantIdx = newMessages.length;
      const updatedMessagesWithPlaceholder = [...newMessages, { role: "model", content: "" } as Message];
      setMessages(updatedMessagesWithPlaceholder);
      
      let assistantMessage = "";

      for await (const chunk of ChatService.sendMessage(userMessage, messages)) {
        assistantMessage += chunk;
        setMessages((prev) => {
          const newMsgs = [...prev];
          const lastMsg = newMsgs[newMsgs.length - 1];
          if (lastMsg && lastMsg.role === "model") {
            lastMsg.content = assistantMessage;
          }
          return [...newMsgs];
        });
      }
      
      // Save after completion
      const finalMessages: Message[] = [...newMessages, { role: "model", content: assistantMessage }];
      saveChat(finalMessages);
      
    } catch (error) {
      console.error("Chat error:", error);
      const errorMsg: Message = { role: "model", content: "Xin lỗi, đã có lỗi xảy ra trong quá trình kết nối." };
      const finalMessagesWithError = [...newMessages, errorMsg];
      setMessages(finalMessagesWithError);
      saveChat(finalMessagesWithError);
    } finally {
      setIsLoading(false);
    }
  };

  const handleEdit = (content: string) => {
    setInputValue(content);
    // Sử dụng setTimeout để đảm bảo React cập nhật state trước khi focus
    setTimeout(() => {
      const inputEl = document.getElementById("chat-input") as HTMLTextAreaElement;
      if (inputEl) {
        inputEl.focus();
        // Đặt con trỏ ở cuối đoạn text
        inputEl.setSelectionRange(inputEl.value.length, inputEl.value.length);
      }
    }, 0);
  };

  return (
    <main className="relative h-screen flex flex-col items-center p-0 overflow-hidden mesh-bg font-sans selection:bg-primary/30">
      <AnimatePresence>
        {messages.length === 0 && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 1 }}
            className="absolute inset-0 z-0"
          >
            <StarfieldBackground starCount={300} speed={0.05} />
          </motion.div>
        )}
      </AnimatePresence>

      {/* Background Decor */}
      <div className="absolute top-[-10%] left-[-10%] w-[60%] h-[60%] bg-primary/5 rounded-full blur-[140px] pointer-events-none animate-pulse-slow" />
      <div className="absolute bottom-[-10%] right-[-10%] w-[60%] h-[60%] bg-accent/5 rounded-full blur-[140px] pointer-events-none animate-pulse-slow [animation-delay:4s]" />

      <Sidebar 
        isOpen={isSidebarOpen} 
        onClose={() => setIsSidebarOpen(false)}
        sessions={sessions}
        currentSessionId={currentSessionId}
        onSelectSession={selectSession}
        onDeleteSession={deleteSession}
        onNewChat={createNewChat}
      />
      
      <ChatHeader 
        onOpenSidebar={() => setIsSidebarOpen(true)} 
        apiMode={process.env.NEXT_PUBLIC_API_MODE} 
      />

      <div className="w-full flex-1 flex flex-col pt-16 z-10 overflow-hidden">
        <AnimatePresence mode="wait">
          {messages.length === 0 ? (
            // Centered Landing Interface (Grok style)
            <motion.div 
              key="landing"
              initial={{ opacity: 0, scale: 0.98 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ 
                opacity: 0, 
                y: -40, 
                scale: 1.05,
                filter: "blur(20px)",
              }}
              transition={{ 
                duration: 0.8, 
                ease: [0.4, 0, 0.2, 1] 
              }}
              className="flex-1 flex flex-col items-center justify-center px-4"
            >
              <div className="w-full max-w-2xl flex flex-col items-center space-y-6">
                {/* Logo / Branding */}
                <div className="flex flex-col items-center space-y-2">
                  <motion.div 
                    initial={{ rotate: 0 }}
                    animate={{ rotate: 12 }}
                    className="w-14 h-14 rounded-2xl bg-gradient-to-br from-primary to-accent flex items-center justify-center shadow-2xl shadow-primary/20 transition-transform hover:rotate-0 duration-500"
                  >
                    <div className="w-6 h-6 border-4 border-white rounded-full border-t-transparent animate-pulse" />
                  </motion.div>
                  <h1 className="text-6xl font-heading font-black tracking-tight text-foreground">
                    DO<span className="text-primary">DO</span>
                  </h1>
                </div>

                {/* Centered Input Area */}
                <div className="w-full max-w-2xl">
                  <ChatInput 
                    value={inputValue} 
                    onChange={setInputValue} 
                    onSend={handleSend} 
                    isLoading={isLoading} 
                  />
                </div>

                {/* Suggestions below input */}
                <div className="flex flex-wrap justify-center gap-2 w-full">
                  {[
                    { title: "Doanh thu", value: "Doanh thu tháng này là bao nhiêu?" },
                    { title: "Khách hàng", value: "Ai là khách hàng tiềm năng nhất?" },
                    { title: "Tồn kho", value: "Báo cáo sản phẩm sắp hết hàng" },
                  ].map((suggestion, i) => (
                    <motion.button 
                      key={i}
                      initial={{ opacity: 0, y: 10 }}
                      animate={{ opacity: 1, y: 0 }}
                      transition={{ 
                        delay: 0.1 * i + 0.4,
                        duration: 0.5,
                        ease: [0.23, 1, 0.32, 1]
                      }}
                      onClick={() => {
                        setInputValue(suggestion.value);
                        setTimeout(() => document.getElementById("chat-input")?.focus(), 0);
                      }}
                      className="px-3 py-1.5 rounded-full glass-panel border border-primary/5 hover:border-primary/20 hover:bg-primary/5 text-[11px] font-medium text-muted-foreground hover:text-primary transition-all duration-300"
                    >
                      {suggestion.title}
                    </motion.button>
                  ))}
                </div>
              </div>
            </motion.div>
          ) : (
            // Normal Chat Interface
            <motion.div 
              key="chat"
              initial={{ opacity: 0, y: 30, scale: 0.99 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              transition={{ 
                duration: 0.7, 
                ease: [0.16, 1, 0.3, 1] // Custom easeOutExpo
              }}
              className="flex-1 flex flex-col overflow-hidden"
            >
              <div 
                ref={scrollRef}
                className="flex-1 overflow-y-auto scrollbar-custom pb-10"
                style={{
                  maskImage: 'linear-gradient(to bottom, transparent, black 60px, black calc(100% - 40px), transparent)',
                  WebkitMaskImage: 'linear-gradient(to bottom, transparent, black 60px, black calc(100% - 40px), transparent)'
                }}
              >
                <div className="max-w-3xl mx-auto w-full px-4 py-8 space-y-8">
                  {messages.map((msg, index) => (
                    <ChatMessage 
                      key={index} 
                      message={msg} 
                      isLoading={isLoading} 
                      isLast={index === messages.length - 1} 
                      onEdit={handleEdit}
                    />
                  ))}
                </div>
              </div>

              <div className="w-full max-w-3xl mx-auto px-4 pb-8 pt-2">
                <ChatInput 
                  value={inputValue} 
                  onChange={setInputValue} 
                  onSend={handleSend} 
                  isLoading={isLoading} 
                />
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </main>
  );
}
