#include <arpa/inet.h>
#include <udt/udt.h>

extern "C" UDT_API int rendezvousConnect(int underlyingSocket, const char * remoteHost, int remotePort) {
    int udtSocket = UDT::socket(AF_INET, SOCK_STREAM, 0);
    if (-1 == udtSocket) {
        return -1;
    }

    bool bTrue = true;
    int setsockoptResult = UDT::setsockopt(udtSocket, 0, UDTOpt::UDT_RENDEZVOUS, &bTrue, -1); // len is ignored by CUDT::setOpt

    int bind2Result = UDT::bind2(udtSocket, underlyingSocket);

    struct sockaddr_in remoteaddr;
    remoteaddr.sin_family = AF_INET;
    remoteaddr.sin_addr.s_addr = inet_addr(remoteHost);
    remoteaddr.sin_port = htons(remotePort);

    try {
        int connectResult = UDT::connect(udtSocket, (struct sockaddr*)&remoteaddr, sizeof(remoteaddr));
        return connectResult;
    }
    catch (...) {
        printf("Exception in rendezvousConnect");
        return -2;
    }
}

