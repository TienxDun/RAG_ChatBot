import * as React from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { Copy, Check, PencilSimple, FileXls, DownloadSimple } from "@phosphor-icons/react";
import { cn } from "@/lib/utils";
import { type Message } from "@/lib/chat-service";
import { TerminalCodeBlock } from "./TerminalCodeBlock";
import { RagSteps } from "./RagSteps";

import { motion } from "framer-motion";

interface ChatMessageProps {
  message: Message;
  isLoading?: boolean;
  isLast?: boolean;
  onEdit?: (content: string) => void;
  onSuggestionClick?: (suggestion: string) => void;
}

export const ChatMessage: React.FC<ChatMessageProps> = ({ message, isLoading, isLast, onEdit, onSuggestionClick }) => {
  const isUser = message.role === "user";
  const [hasCopied, setHasCopied] = React.useState(false);

  const onCopy = React.useCallback(() => {
    if (!message.content) return;
    navigator.clipboard.writeText(message.content);
    setHasCopied(true);
    setTimeout(() => setHasCopied(false), 2000);
  }, [message.content]);

  const onDownloadExcel = React.useCallback(async () => {
    try {
      if (message.excelBase64) {
        // Nhánh 1: Có template đã fill → decode Base64 trực tiếp
        const byteCharacters = atob(message.excelBase64);
        const byteNumbers = new Array(byteCharacters.length);
        for (let i = 0; i < byteCharacters.length; i++) {
          byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        const byteArray = new Uint8Array(byteNumbers);
        const blob = new Blob([byteArray], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });
        const link = document.createElement('a');
        link.href = window.URL.createObjectURL(blob);
        link.download = `filled_report_${new Date().getTime()}.xlsx`;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
      } else if (message.rawData) {
        // Nhánh 2: Không có template → gọi API export từ data thô
        const { saveAs } = await import("file-saver");
        const baseUrl = process.env.NEXT_PUBLIC_DOTNET_API_URL || 'http://localhost:5000/api/chat';
        const exportUrl = baseUrl.endsWith('/chat') ? `${baseUrl}/export-excel` : `${baseUrl}/chat/export-excel`;
        
        const response = await fetch(exportUrl, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: message.rawData
        });

        if (!response.ok) {
          throw new Error("Export failed on server");
        }

        const blob = await response.blob();
        saveAs(blob, `data_export_${new Date().getTime()}.xlsx`);
      }
    } catch (e) {
      console.error("Download Excel error:", e);
      alert("Lỗi: Không thể tải file Excel.");
    }
  }, [message.excelBase64, message.rawData]);

  return (
    <motion.div 
      initial={{ opacity: 0, y: 20, scale: 0.95 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      transition={{ 
        duration: 0.4,
        ease: [0.23, 1, 0.32, 1] // Custom cubic-bezier for smooth easing
      }}
      className={cn(
        "flex w-full group",
        isUser ? "justify-end" : "justify-start"
      )}
    >
      <div className={cn(
        "relative flex w-fit max-w-full",
        isUser ? "flex-row-reverse items-start gap-2" : "flex-col"
      )}>
        <div className={cn(
          "relative rounded-2xl p-5 md:p-6 text-[15px] md:text-base leading-relaxed transition-all shadow-lg hover:shadow-xl",
          isUser 
            ? "bg-white text-foreground border border-black/5 rounded-tr-none" 
            : "glass-panel border border-primary/10 text-foreground rounded-tl-none"
        )}>
          {isUser ? (
            <div className="whitespace-pre-wrap font-semibold text-foreground/90">{message.content}</div>
          ) : (
            <div className="flex flex-col">
              <div className="markdown-content prose-headings:font-heading prose-headings:font-bold">
                {message.content ? (
                  <ReactMarkdown 
                    remarkPlugins={[remarkGfm]}
                    components={{
                      pre({ children }) {
                        return <>{children}</>;
                      },
                      code({ node, className, children, ...props }) {
                        const match = /language-(\w+)/.exec(className || "");
                        const isInline = !className;
                        
                        if (!isInline) {
                          return (
                            <TerminalCodeBlock 
                              language={match?.[1]} 
                              value={String(children).replace(/\n$/, "")} 
                            />
                          );
                        }

                        return (
                          <code className={cn("px-1.5 py-0.5 rounded bg-primary/10 text-primary font-mono text-[0.9em]", className)} {...props}>
                            {children}
                          </code>
                        );
                      },
                      table({ children }) {
                        return (
                          <div className="w-full overflow-x-auto scrollbar-custom my-6 rounded-xl border border-primary/10 shadow-sm">
                            <table className="w-full border-collapse text-sm">
                              {children}
                            </table>
                          </div>
                        );
                      }
                    }}
                  >
                    {message.content}
                  </ReactMarkdown>
                ) : isLoading && isLast && (
                  <div className="flex gap-2 py-2">
                    <span className="w-2 h-2 bg-primary/40 rounded-full animate-bounce [animation-delay:-0.3s]" />
                    <span className="w-2 h-2 bg-primary/40 rounded-full animate-bounce [animation-delay:-0.15s]" />
                    <span className="w-2 h-2 bg-primary/40 rounded-full animate-bounce" />
                  </div>
                )}
              </div>
              
              {message.steps && message.steps.length > 0 && (
                <RagSteps steps={message.steps} />
              )}
              
              {message.content && (
                <div className="mt-5 pt-4 border-t border-primary/10 flex items-center gap-3 animate-in fade-in duration-500">
                  <div className="px-2 py-0.5 rounded bg-primary/10 text-[10px] font-bold text-primary uppercase tracking-tighter">AI INSIGHT</div>
                  <div className="text-[10px] text-muted-foreground/40 font-medium flex-1 hidden xs:block">Chat can make mistakes. Check important info.</div>
                  <button 
                    onClick={onCopy} 
                    className="text-muted-foreground/40 hover:text-primary transition-colors flex items-center gap-1.5 text-[11px] font-medium"
                  >
                    {hasCopied ? <Check size={14} weight="bold" className="text-green-500" /> : <Copy size={14} />} 
                    {hasCopied ? "Đã copy" : "Copy"}
                  </button>

                  {(message.rawData || message.excelBase64) && (
                    <button 
                      onClick={onDownloadExcel} 
                      className="text-emerald-500/80 hover:text-emerald-600 transition-colors flex items-center gap-1.5 text-[11px] font-bold bg-emerald-50 px-2.5 py-1 rounded-lg border border-emerald-100 shadow-sm"
                      title={message.excelBase64 ? "Tải file Excel đã điền dữ liệu" : "Tải kết quả xuống dạng Excel (.xlsx)"}
                    >
                      <DownloadSimple size={16} weight="bold" />
                      {message.excelBase64 ? "Tải báo cáo" : "Xuất Excel"}
                    </button>
                  )}
                </div>
              )}
            </div>
          )}
        </div>

        {!isUser && message.suggestedQuestions && message.suggestedQuestions.length > 0 && (
          <div className="flex flex-col gap-2 mt-4 pl-1 items-start">
            {message.suggestedQuestions.map((q, i) => (
              <motion.button
                key={i}
                initial={{ opacity: 0, x: -20 }}
                animate={{ opacity: 1, x: 0 }}
                transition={{ 
                  delay: 0.1 * i + 0.3,
                  type: "spring",
                  stiffness: 260,
                  damping: 20
                }}
                onClick={() => onSuggestionClick?.(q)}
                className="px-3 py-1.5 rounded-xl glass-panel border border-primary/5 hover:border-primary/30 hover:bg-primary/10 text-xs font-medium text-muted-foreground hover:text-primary transition-all duration-300 flex items-start gap-2 group/btn text-left"
              >
                <span className="w-1.5 h-1.5 mt-1 shrink-0 rounded-full bg-primary/40 group-hover/btn:bg-primary transition-colors" />
                <span>{q}</span>
              </motion.button>
            ))}
          </div>
        )}

        {isUser && (
          <div className="flex flex-col gap-1 opacity-0 group-hover:opacity-100 transition-opacity duration-200 py-1">
            {onEdit && (
              <button 
                onClick={() => onEdit(message.content)} 
                className="w-8 h-8 flex items-center justify-center rounded-full bg-white border border-black/5 text-foreground/30 hover:text-primary hover:border-primary/30 transition-all shadow-sm"
                title="Sửa tin nhắn"
              >
                <PencilSimple size={14} />
              </button>
            )}
            <button 
              onClick={onCopy} 
              className="w-8 h-8 flex items-center justify-center rounded-full bg-white border border-black/5 text-foreground/30 hover:text-primary hover:border-primary/30 transition-all shadow-sm"
              title="Copy tin nhắn"
            >
               {hasCopied ? <Check size={14} weight="bold" className="text-green-500" /> : <Copy size={14} />} 
            </button>
          </div>
        )}
      </div>
    </motion.div>
  );
};
