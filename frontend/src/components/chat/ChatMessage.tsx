import * as React from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { Copy, Check, PencilSimple } from "@phosphor-icons/react";
import { cn } from "@/lib/utils";
import { type Message } from "@/lib/chat-service";
import { TerminalCodeBlock } from "./TerminalCodeBlock";

interface ChatMessageProps {
  message: Message;
  isLoading?: boolean;
  isLast?: boolean;
  onEdit?: (content: string) => void;
}

export const ChatMessage: React.FC<ChatMessageProps> = ({ message, isLoading, isLast, onEdit }) => {
  const isUser = message.role === "user";
  const [hasCopied, setHasCopied] = React.useState(false);

  const onCopy = React.useCallback(() => {
    if (!message.content) return;
    navigator.clipboard.writeText(message.content);
    setHasCopied(true);
    setTimeout(() => setHasCopied(false), 2000);
  }, [message.content]);

  return (
    <div className={cn(
      "flex w-full animate-in fade-in slide-in-from-bottom-4 duration-500 group",
      isUser ? "justify-end" : "justify-start"
    )}>
      <div className={cn(
        "relative flex max-w-full md:max-w-[85%]",
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
              
              {message.content && (
                <div className="mt-5 pt-4 border-t border-primary/10 flex items-center gap-3 animate-in fade-in duration-500">
                  <div className="px-2 py-0.5 rounded bg-primary/10 text-[10px] font-bold text-primary uppercase tracking-tighter">AI INSIGHT</div>
                  <div className="text-[10px] text-muted-foreground/40 font-medium flex-1">Chat can make mistakes. Check important info.</div>
                  <button 
                    onClick={onCopy} 
                    className="text-muted-foreground/40 hover:text-primary transition-colors flex items-center gap-1.5 text-[11px] font-medium"
                  >
                    {hasCopied ? <Check size={14} weight="bold" className="text-green-500" /> : <Copy size={14} />} 
                    {hasCopied ? "Đã copy" : "Copy"}
                  </button>
                </div>
              )}
            </div>
          )}
        </div>

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
    </div>
  );
};
