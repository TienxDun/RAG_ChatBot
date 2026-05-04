import type { Metadata } from "next";
import { Be_Vietnam_Pro, JetBrains_Mono } from "next/font/google";
import "./globals.css";

const beVietnamPro = Be_Vietnam_Pro({
  variable: "--font-be-vietnam",
  subsets: ["vietnamese", "latin"],
  weight: ["100", "200", "300", "400", "500", "600", "700", "800", "900"],
});

const jetBrainsMono = JetBrains_Mono({
  variable: "--font-mono",
  subsets: ["latin", "vietnamese"],
});

export const metadata: Metadata = {
  title: "RAG ChatBot - AI Intelligent Assistant",
  description: "Hệ thống ChatBot thông minh sử dụng công nghệ RAG (Retrieval-Augmented Generation)",
  icons: {
    icon: "/icon.svg",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="vi">
      <body
        className={`${beVietnamPro.variable} ${jetBrainsMono.variable} min-h-full flex flex-col font-sans bg-background text-foreground antialiased`}
      >
        {children}
      </body>
    </html>
  );
}
