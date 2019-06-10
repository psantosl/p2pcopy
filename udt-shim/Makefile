all: udt-shim.cpp 
	gcc -shared -o ../bin/Debug/libudt-shim.so -fPIC udt-shim.cpp -lstdc++ -ludt

clean: 
	$(RM) ../bin/Debug/libudt-shim.so

