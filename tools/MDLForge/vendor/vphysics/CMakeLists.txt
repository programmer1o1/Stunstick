add_library(mdlforge_vphysics SHARED
    src/vphysics_module.cpp
    src/cphysconvex.cpp
    src/cphyscollide.cpp
    src/phy_serialize.cpp
    src/surfaceprops.cpp
    ../../utils/studiomdl/tier0_stubs.cpp
    ../quickhull/QuickHull.cpp
)

target_include_directories(mdlforge_vphysics PRIVATE
    ${PROJECT_SOURCE_DIR}/public
    ${PROJECT_SOURCE_DIR}/public/mathlib
    ${PROJECT_SOURCE_DIR}/public/tier0
    ${PROJECT_SOURCE_DIR}/public/tier1
    ${PROJECT_SOURCE_DIR}/mathlib
    ../quickhull
)
target_compile_definitions(mdlforge_vphysics PRIVATE $<TARGET_PROPERTY:studiomdl_mathlib,COMPILE_DEFINITIONS>)
target_link_libraries(mdlforge_vphysics PRIVATE studiomdl_mathlib)

set_target_properties(mdlforge_vphysics PROPERTIES
    OUTPUT_NAME vphysics
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON
    LIBRARY_OUTPUT_DIRECTORY $<TARGET_FILE_DIR:studiomdl>
    RUNTIME_OUTPUT_DIRECTORY $<TARGET_FILE_DIR:studiomdl>
)

include(GNUInstallDirs)
install(TARGETS mdlforge_vphysics
    LIBRARY DESTINATION ${CMAKE_INSTALL_BINDIR}
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
)
install(FILES LICENSE DESTINATION ${CMAKE_INSTALL_BINDIR} RENAME vphysics-LICENSE.txt)
